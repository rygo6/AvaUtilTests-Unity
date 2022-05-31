using System;
using System.Collections.Generic;
using GeoTetra.GTSplines;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Splines;

namespace GeoTetra.GTDoppel
{
    [RequireComponent(typeof(LineRenderer), typeof(SplineContainer))]
    public class DoppelSpline : MonoBehaviour
    {
        // [SerializeField]
        // SplineContainer m_SplineContainer;
        
        [SerializeField]
        LineRenderer m_Line;

        [SerializeField] 
        MeshCollider m_MeshCollider;

        [SerializeField] 
        float m_StepSize = .1f;
        
        bool m_Dirty;
        NativeList<Vector3> m_InterpolatedPoints; 
        public UnsafeNativeSpline m_NativeSpline;

        public LineRenderer Line => m_Line;

        public UnsafeNativeSpline NativeSpline => m_NativeSpline;

        void Awake()
        {
            m_InterpolatedPoints = new NativeList<Vector3>(128,Allocator.Persistent);
            m_NativeSpline = new UnsafeNativeSpline(8, Allocator.Persistent);
            // m_SplineContainer.Spline.changed += SplineOnChanged;
        }

        void OnDestroy()
        {
            m_InterpolatedPoints.Dispose();
            m_NativeSpline.Dispose();
        }

        void OnValidate()
        {
            // m_SplineContainer = GetComponent<SplineContainer>();
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
        }
    }
}