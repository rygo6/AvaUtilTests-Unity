using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Splines;

namespace GeoTetra.GTDoppel
{
    [RequireComponent(typeof(LineRenderer), typeof(SplineContainer))]
    public class DoppelSpline : MonoBehaviour
    {
        [SerializeField]
        SplineContainer m_SplineContainer;
        
        [SerializeField]
        LineRenderer m_Line;

        [SerializeField] 
        float m_StepSize = .1f;
        
        bool m_Dirty;
        NativeList<Vector3> m_Points;

        public SplineContainer Container => m_SplineContainer;

        public LineRenderer Line => m_Line;

        void Awake()
        {
            m_Points = new NativeList<Vector3>(128,Allocator.Persistent);
            m_SplineContainer.Spline.changed += SplineOnChanged;
        }

        void OnDestroy()
        {
            m_Points.Dispose();
        }

        void OnValidate()
        {
            m_SplineContainer = GetComponent<SplineContainer>();
            m_Line = GetComponent<LineRenderer>();
        }

        void SplineOnChanged()
        {
            m_Points.Clear();
            float curveLength = m_SplineContainer.Spline.GetLength();
            int count = (int)(curveLength / m_StepSize);
            for (int i = 0; i < count; ++i)
            {
                m_Points.Add(m_SplineContainer.Spline.EvaluatePosition(i / (count - 1f)));
            }

            m_Line.positionCount = count;
            m_Line.SetPositions(m_Points);
        }
    }
}