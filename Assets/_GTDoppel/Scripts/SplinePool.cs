using System;
using System.Collections.Generic;
using GeoTetra.GTSplines;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;

namespace GeoTetra.GTDoppel
{
    public class SplinePool : MonoBehaviour
    {
        [SerializeField] 
        DoppelSpline m_SplineContainerPrefab;

        List<DoppelSpline> m_Splines = new();

        NativeList<UnsafeNativeSpline> m_NativeSplines;
        NativeList<float> m_Distances;
        NativeList<Vector3> m_NearestPoints;
        RaycastSplinesJob m_RaycastSplinesJob;
        JobHandle m_RaycastSplinesJobHandle;
        
        struct RaycastSplinesJob : IJobParallelFor
        {
            [ReadOnly]
            public Ray InputRay;

            [ReadOnly]
            public NativeArray<UnsafeNativeSpline> Splines;

            [WriteOnly] 
            public NativeArray<Vector3> NearestPoints;
            
            [WriteOnly] 
            public NativeArray<float> Distances;

            public void Execute(int index)
            {
                float distance = SplineUtility.GetNearestPoint(Splines[index], InputRay, out float3 nearest, out float t);
                NearestPoints[index] = nearest;
                Distances[index] = distance;
            }
        }

        void Awake()
        {
            m_NativeSplines = new NativeList<UnsafeNativeSpline>(m_Splines.Count, Allocator.Persistent);
            m_Distances = new NativeList<float>(m_Splines.Count, Allocator.Persistent);
            m_NearestPoints = new NativeList<Vector3>(m_Splines.Count, Allocator.Persistent);
        }

        void OnDestroy()
        {
            m_NativeSplines.Dispose();
            m_Distances.Dispose();
            m_NearestPoints.Dispose();
        }

        public DoppelSpline CreateSpline(Vector3 startPoint, Quaternion startRotation)
        {
            var splineInstance = Instantiate(m_SplineContainerPrefab);
            splineInstance.AddKnot(new BezierKnot(startPoint, 0, 0, startRotation));
            m_Splines.Add(splineInstance);
            // m_NativeSplines.Add(splineInstance.m_NativeSpline);
            m_Distances.Length = m_Splines.Count;
            m_NearestPoints.Length = m_Splines.Count;
            return splineInstance;
        }

        void Update()
        {
            m_RaycastSplinesJobHandle.Complete();
            for (int i = 0; i < m_NearestPoints.Length; ++i)
            {
                Debug.Log(m_NearestPoints[i]);
                Debug.DrawRay(m_NearestPoints[i], Vector3.up * .1f, Color.blue);
            }

            var ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            Debug.DrawRay(ray.origin, ray.direction, Color.red);
            
            
            m_NativeSplines.Clear();
            for (int i = 0; i < m_Splines.Count; ++i)
            {
                // you must copy every timne because only the buffer data is on heap
                // count etc isnt
                m_NativeSplines.Add(m_Splines[i].m_NativeSpline);
            }

            m_RaycastSplinesJob = new RaycastSplinesJob
            {
                InputRay = ray,
                Splines = m_NativeSplines,
                NearestPoints = m_NearestPoints,
                Distances = m_Distances
            };
            m_RaycastSplinesJobHandle = m_RaycastSplinesJob.Schedule(m_Splines.Count, 1);
        }
    }
}