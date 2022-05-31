using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine.Splines;

namespace GeoTetra.GTSplines
{
    /// <summary>
    /// A readonly representation of <see cref="Spline"/> that is optimized for efficient access and queries.
    /// NativeSpline can be constructed with a Spline and Transform. If a transform is applied, all values will be
    /// relative to the transformed knot positions.
    /// </summary>
    /// <remarks>
    /// NativeSpline is compatible with the job system.
    /// </remarks>
    public struct UnsafeNativeSpline : ISpline, IDisposable
    {
        UnsafeList<BezierKnot> m_Knots;
        // As we cannot make a NativeArray of NativeArray all segments lookup tables are stored in a single array
        // each lookup table as a length of k_SegmentResolution and starts at index i = curveIndex * k_SegmentResolution
        UnsafeList<DistanceToInterpolation> m_SegmentLengthsLookupTable;
        bool m_Closed;
        float m_Length;
        const int k_SegmentResolution = 30;

        /// <summary>
        /// A NativeArray of <see cref="BezierKnot"/> that form this Spline.
        /// </summary>
        /// <returns>
        /// Returns a reference to the knots array.
        /// </returns>
        public UnsafeList<BezierKnot> Knots => m_Knots;

        /// <summary>
        /// Whether the spline is open (has a start and end point) or closed (forms an unbroken loop).
        /// </summary>
        public bool Closed => m_Closed;

        /// <summary>
        /// Return the number of knots.
        /// </summary>
        public int Count => m_Knots.Length;

        /// <summary>
        /// Return the sum of all curve lengths, accounting for <see cref="Closed"/> state.
        /// Note that this value is affected by the transform used to create this NativeSpline.
        /// </summary>
        /// <returns>
        /// Returns the sum length of all curves composing this spline, accounting for closed state.
        /// </returns>
        public float GetLength() => m_Length;

        /// <summary>
        /// Get the knot at <paramref name="index"/>.
        /// </summary>
        /// <param name="index">The zero-based index of the knot.</param>
        public BezierKnot this[int index] => m_Knots[index];

        /// <summary>
        /// Get an enumerator that iterates through the <see cref="BezierKnot"/> collection.
        /// </summary>
        /// <returns>An IEnumerator that is used to iterate the <see cref="BezierKnot"/> collection.</returns>
        public IEnumerator<BezierKnot> GetEnumerator() => m_Knots.GetEnumerator();
            
        /// <inheritdoc cref="GetEnumerator"/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public UnsafeNativeSpline(int initialCapacity, Allocator allocator = Allocator.Temp)
        {
            m_Knots = new UnsafeList<BezierKnot>(initialCapacity, allocator);
            m_Closed = false;
            m_Length = 0f;
            m_SegmentLengthsLookupTable = new UnsafeList<DistanceToInterpolation>(initialCapacity * k_SegmentResolution, allocator);
        }

        /// <summary>
        /// Create a new NativeSpline from a set of <see cref="BezierKnot"/>.
        /// </summary>
        /// <param name="spline">The <see cref="ISpline"/> object to convert to a <see cref="NativeSpline"/>.</param>
        /// <param name="allocator">The memory allocation method to use when reserving space for native arrays.</param>
        public UnsafeNativeSpline(ISpline spline, Allocator allocator = Allocator.Temp)
            : this(spline, spline.Closed, float4x4.identity, allocator) { }

        /// <summary>
        /// Create a new NativeSpline from a set of <see cref="BezierKnot"/>.
        /// </summary>
        /// <param name="spline">The <see cref="ISpline"/> object to convert to a <see cref="NativeSpline"/>.</param>
        /// <param name="transform">A transform matrix to be applied to the spline knots and tangents.</param>
        /// <param name="allocator">The memory allocation method to use when reserving space for native arrays.</param>
        public UnsafeNativeSpline(ISpline spline, float4x4 transform, Allocator allocator = Allocator.Temp)
            : this(spline, spline.Closed, transform, allocator) { }

        /// <summary>
        /// Create a new NativeSpline from a set of <see cref="BezierKnot"/>.
        /// </summary>
        /// <param name="knots">A collection of sequential <see cref="BezierKnot"/> forming the spline path.</param>
        /// <param name="closed">Whether the spline is open (has a start and end point) or closed (forms an unbroken loop).</param>
        /// <param name="transform">Apply a transformation matrix to the control <see cref="Knots"/>.</param>
        /// <param name="allocator">The memory allocation method to use when reserving space for native arrays.</param>
        public UnsafeNativeSpline(IReadOnlyList<BezierKnot> knots, bool closed, float4x4 transform, Allocator allocator = Allocator.Temp)
        {
            int kc = knots.Count;
            m_Knots = new UnsafeList<BezierKnot>(knots.Count, allocator);
            m_Knots.Length = knots.Count;

            for (int i = 0; i < kc; i++)
                m_Knots[i] = knots[i].Transform(transform);

            m_Closed = closed;
            m_Length = 0f;
            
            m_SegmentLengthsLookupTable = new UnsafeList<DistanceToInterpolation>(knots.Count * k_SegmentResolution, allocator);

            EnsureCurveLengthCacheValid();
        }
        
        public void AddKnot(BezierKnot item)
        {
            m_Knots.Add(item);
            SetDirty();
        }
        
        public void UpdateKnot(int index, BezierKnot item)
        {
            m_Knots[index] = item;
            SetDirty();
        }

        /// <summary>
        /// Get a <see cref="BezierCurve"/> from a knot index.
        /// </summary>
        /// <param name="index">The knot index that serves as the first control point for this curve.</param>
        /// <returns>
        /// A <see cref="BezierCurve"/> formed by the knot at index and the next knot.
        /// </returns>
        public BezierCurve GetCurve(int index)
        {
            int next = m_Closed ? (index + 1) % Count : math.min(index + 1, Count - 1);
            return new BezierCurve(m_Knots[index], m_Knots[next]);
        }

        /// <summary>
        /// Get the length of a <see cref="BezierCurve"/>.
        /// </summary>
        /// <param name="curveIndex">The 0 based index of the curve to find length for.</param>
        /// <returns>The length of the bezier curve at index.</returns>
        public float GetCurveLength(int curveIndex) => m_SegmentLengthsLookupTable[curveIndex * k_SegmentResolution + k_SegmentResolution - 1].Distance;

        /// <summary>
        /// Release allocated resources.
        /// </summary>
        public void Dispose()
        {
            m_Knots.Dispose();
            m_SegmentLengthsLookupTable.Dispose();
        }

        struct UnsafeSlice<T> : IReadOnlyList<T> where T : unmanaged
        {
            readonly unsafe byte* m_Buffer;
            readonly int m_Stride;
            readonly int m_Length;
            readonly int m_MinIndex;
            readonly int m_MaxIndex;
            
            public unsafe UnsafeSlice(UnsafeList<T> array, int start, int count)
            {
                m_Stride = UnsafeUtility.SizeOf<T>();
                m_Length = count;
                m_Buffer = (byte*) array.Ptr + m_Stride * start;
                m_MinIndex = 0;
                m_MaxIndex = m_Length - 1;
            }
            
            public IEnumerator<T> GetEnumerator() => new Enumerator(ref this);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public int Count => m_Length;
            
            public unsafe T this[int index]
            {
                get
                {
                    CheckIndex(index);
                    return UnsafeUtility.ReadArrayElementWithStride<T>(m_Buffer, index, m_Stride);
                }
                [WriteAccessRequired] set
                {
                    CheckIndex(index);
                    UnsafeUtility.WriteArrayElementWithStride<T>(m_Buffer, index, m_Stride, value);
                }
            }
            
            void CheckIndex(int index)
            {
                if (index < m_MinIndex || index > m_MaxIndex)
                    FailOutOfRangeError(index);
            }
            
            void FailOutOfRangeError(int index)
            {
                if (index < m_Length && (this.m_MinIndex != 0 || this.m_MaxIndex != m_Length - 1))
                    throw new IndexOutOfRangeException(string.Format("Index {0} is out of restricted IJobParallelFor range [{1}...{2}] in ReadWriteBuffer.\n", (object) index, (object) this.m_MinIndex, (object) this.m_MaxIndex) + "ReadWriteBuffers are restricted to only read & write the element at the job index. You can use double buffering strategies to avoid race conditions due to reading & writing in parallel to the same elements from a job.");
                throw new IndexOutOfRangeException(string.Format("Index {0} is out of range of '{1}' Length.", (object) index, (object) m_Length));
            }
            
            struct Enumerator : IEnumerator<T>, IEnumerator, IDisposable
            {
                UnsafeSlice<T> m_Array;
                int m_Index;

                public Enumerator(ref UnsafeSlice<T> array)
                {
                    m_Array = array;
                    m_Index = -1;
                }

                public void Dispose()
                { }

                public bool MoveNext()
                {
                    ++this.m_Index;
                    return this.m_Index < this.m_Array.m_Length;
                }

                public void Reset() => this.m_Index = -1;

                public T Current => this.m_Array[this.m_Index];

                object IEnumerator.Current => (object) this.Current;
            }
        }

        /// <summary>
        /// Return the normalized interpolation (t) corresponding to a distance on a <see cref="BezierCurve"/>.
        /// </summary>
        /// <param name="curveIndex"> The zero-based index of the curve.</param>
        /// <param name="curveDistance">The curve-relative distance to convert to an interpolation ratio (also referred to as 't').</param>
        /// <returns>  The normalized interpolation ratio associated to distance on the designated curve.</returns>
        public float GetCurveInterpolation(int curveIndex, float curveDistance)
        {
            if(curveIndex <0 || curveIndex >= m_SegmentLengthsLookupTable.Length || curveDistance <= 0)
                return 0f;
            var curveLength = GetCurveLength(curveIndex);
            if(curveDistance >= curveLength)
                return 1f;
            var startIndex = curveIndex * k_SegmentResolution;
            var slice = new UnsafeSlice<DistanceToInterpolation>(m_SegmentLengthsLookupTable, startIndex, k_SegmentResolution);
            return CurveUtility.GetDistanceToInterpolation(slice, curveDistance);
        }
        
        void SetDirty()
        {
            EnsureCurveLengthCacheValid();
        }

        void EnsureCurveLengthCacheValid()
        {
            var lookupTableLength = m_Knots.Length * k_SegmentResolution;
            if (m_SegmentLengthsLookupTable.Length != lookupTableLength)
            {
                m_SegmentLengthsLookupTable.Length = lookupTableLength;
                
                int curveCount = m_Closed ? m_Knots.Length : m_Knots.Length - 1;
                
                m_Length = 0f;

                DistanceToInterpolation[] distanceToTimes = new DistanceToInterpolation[k_SegmentResolution];

                for (int i = 0; i < curveCount; i++)
                {
                    CurveUtility.CalculateCurveLengths(GetCurve(i), distanceToTimes);
                    m_Length += distanceToTimes[k_SegmentResolution - 1].Distance;
                    for(int distanceToTimeIndex = 0; distanceToTimeIndex < k_SegmentResolution; distanceToTimeIndex++)
                        m_SegmentLengthsLookupTable[i * k_SegmentResolution + distanceToTimeIndex] = distanceToTimes[distanceToTimeIndex];
                }
            }
        }
    }
}
