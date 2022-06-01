using System;
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

        public event Action<GTSplineContainer> OnChanged;
        
        NativeList<Vector3> m_InterpolatedPoints; 
        GTUnsafeNativeSpline m_NativeSpline;

        public LineRenderer Line => m_Line;
        public GTUnsafeNativeSpline NativeSpline => m_NativeSpline;
        public GTSplinePool ParentSplinePool { get; private set; }
        public int ParentSplinePoolIndex { get; private set; }

        void Awake()
        {
            m_InterpolatedPoints = new NativeList<Vector3>(128,Allocator.Persistent);
            m_NativeSpline = new GTUnsafeNativeSpline(8, Allocator.Persistent);
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
        }

        void OnValidate()
        {
            m_Line = GetComponent<LineRenderer>();
        }

        public void AddKnot(BezierKnot item)
        {
            m_NativeSpline.AddKnot(item);
            SplineOnChanged();
        }
        
        public void UpdateKnot(int index, BezierKnot item)
        {
            m_NativeSpline.UpdateKnot(index, item);
            SplineOnChanged();
        }
        
        void SplineOnChanged()
        {
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
            throw new NotImplementedException();
        }
    }
}