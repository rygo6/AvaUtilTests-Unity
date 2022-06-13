using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Splines;

namespace GeoTetra.GTSplines
{
    public class GTSplinePool : MonoBehaviour
    {
        [SerializeField] GTSplineContainer m_SplineContainerContainerPrefab;

        List<GTSplineContainer> m_Splines = new();

        Queue<GTSplineContainer> m_DirtySplines = new();

        NativeList<GTUnsafeNativeSpline> m_NativeSplines;
        public List<GTSplineContainer> Splines => m_Splines;

        public event Action<GTSplineContainer> OnSplineDirty;

        void Awake()
        {
            m_NativeSplines = new NativeList<GTUnsafeNativeSpline>(m_Splines.Count, Allocator.Persistent);
        }

        void OnDestroy()
        {
            m_NativeSplines.Dispose();
        }

        public GTSplineContainer CreateSpline(Vector3 startPoint, Quaternion startRotation)
        {
            var splineInstance = Instantiate(m_SplineContainerContainerPrefab);
            splineInstance.Initialize(this, m_Splines.Count);
            splineInstance.OnChanged += SplineInstanceOnOnChanged;
            m_Splines.Add(splineInstance);
            m_DirtySplines.Enqueue(splineInstance);
            splineInstance.AddKnot(new BezierKnot(startPoint, 0, 0, startRotation));
            OnSplineDirty?.Invoke(splineInstance);
            return splineInstance;
        }

        public NativeList<GTUnsafeNativeSpline> GetNativeSplinesAndUpdateDirty()
        {
            // you must update the native splines here in between the jobs running
            // also must copy native spline each time it changes as only the buffer in unsafeNativeSpline is on heap
            while (m_DirtySplines.Count > 0)
            {
                var spline = m_DirtySplines.Dequeue();
                if (m_NativeSplines.Length <= spline.ParentSplinePoolIndex)
                    m_NativeSplines.Add(spline.NativeSpline);
                else
                    m_NativeSplines[spline.ParentSplinePoolIndex] = spline.NativeSpline;
            }

            return m_NativeSplines;
        }

        void SplineInstanceOnOnChanged(GTSplineContainer spline)
        {
            m_DirtySplines.Enqueue(spline);
            OnSplineDirty?.Invoke(spline);
        }
    }
}