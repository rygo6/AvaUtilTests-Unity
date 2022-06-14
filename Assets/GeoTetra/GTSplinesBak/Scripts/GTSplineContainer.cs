using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Splines;

namespace GeoTetra.GTSplines
{
    [RequireComponent(typeof(LineRenderer))]
    public class GTSplineContainer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [SerializeField]
        LineRenderer m_Line;
        
        [SerializeField] 
        float m_StepSize = .1f;

        [SerializeField] 
        Transform m_SplinePointPrefab;
        public event Action<GTSplineContainer> OnChanged;
        
        readonly List<Transform> m_SplinePoints = new();
        NativeList<Vector3> m_InterpolatedPoints;
        NativeList<float> m_Lengths;
        NativeList<BezierCurve> m_Curves;
        GTUnsafeNativeSpline m_NativeSpline;

        public GTSplinePool ParentSplinePool { get; private set; }
        public int ParentSplinePoolIndex { get; private set; }
        public LineRenderer Line => m_Line;
        public GTUnsafeNativeSpline NativeSpline => m_NativeSpline;
        public NativeList<Vector3> InterpolatedPoints => m_InterpolatedPoints;
        public NativeList<float> Lengths => m_Lengths;

        public NativeList<BezierCurve> Curves => m_Curves;
        
        public Vector4 Info => new Vector4(m_NativeSpline.Count, m_NativeSpline.Closed ? 1 : 0, m_NativeSpline.GetLength(), 0);

        void Awake()
        {
            m_InterpolatedPoints = new NativeList<Vector3>(128,Allocator.Persistent);
            m_NativeSpline = new GTUnsafeNativeSpline(8, Allocator.Persistent);
            m_Lengths = new NativeList<float>(8, Allocator.Persistent);
            m_Curves = new NativeList<BezierCurve>(8, Allocator.Persistent);
        }
        
        public void Initialize(GTSplinePool parentSplinePool, int parentSplinePoolIndex)
        {
            ParentSplinePool = parentSplinePool;
            ParentSplinePoolIndex = parentSplinePoolIndex;
        }

        void OnDestroy()
        {
            m_InterpolatedPoints.Dispose();
            m_NativeSpline.Dispose();
            m_Lengths.Dispose();
            m_Curves.Dispose();
        }

        void OnValidate()
        {
            m_Line = GetComponent<LineRenderer>();
        }

        public void AddKnot(BezierKnot knot)
        {
            m_NativeSpline.AddKnot(knot);
            var splinePoint = Instantiate(m_SplinePointPrefab, knot.Position, knot.Rotation);
            m_SplinePoints.Add(splinePoint);
            SplineOnChanged();
        }
        
        public void UpdateKnot(int index, BezierKnot knot)
        {
            m_NativeSpline.UpdateKnot(index, knot);
            m_SplinePoints[index].position = knot.Position;
            m_SplinePoints[index].rotation = knot.Rotation;
            SplineOnChanged();
        }
        
        void SplineOnChanged()
        {
            // Could be faster? Only update specific curve/length via Update knot?
            m_Lengths.Clear();
            m_Curves.Clear();
            for (int i = 0; i < m_NativeSpline.Count; ++i)
            {
                m_Lengths.Add(m_NativeSpline.GetCurveLength(i));
                m_Curves.Add(m_NativeSpline.GetCurve(i));
            }
            
            // Use InterpolateCompute shader?
            m_InterpolatedPoints.Clear();
            float curveLength = m_NativeSpline.GetLength();
            int count = (int)(curveLength / m_StepSize);
            for (int i = 0; i < count; ++i)
            {
                m_InterpolatedPoints.Add(m_NativeSpline.EvaluatePosition(i / (count - 1f)));
            }

            m_Line.positionCount = count;
            m_Line.SetPositions(m_InterpolatedPoints);
            
            OnChanged?.Invoke(this);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            m_Line.startColor = Color.green;
            m_Line.endColor = Color.green;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            m_Line.startColor = Color.gray;
            m_Line.endColor = Color.gray;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // throw new NotImplementedException();
        }

        public GTSplineData Serialize()
        {
            var data = new GTSplineData
            {
                Knots = m_NativeSpline.Knots.ToArray()
            };
            return data;
        }
        
        public void Deserialize(GTSplineData data)
        {
            for (int i = 0; i < data.Knots.Length; ++i)
            {
                AddKnot(data.Knots[i]);   
            }
        }

        [Serializable]
        public struct GTSplineData
        {
            public int Guid;
            public BezierKnot[] Knots;
        }
    }
}