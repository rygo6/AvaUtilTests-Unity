using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Splines;

namespace GeoTetra.GTSplines
{
    /// <summary>
    /// Simple event system using physics raycasts.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class GTSplineRaycaster : BaseRaycaster
    {
        [SerializeField] 
        float m_SelectScreenDistance = 4f;
        
        [SerializeField] 
        GTSplinePool m_SplinePool;
        
        /// <summary>
        /// Const to use for clarity when no event mask is set
        /// </summary>
        protected const int kNoEventMaskSet = -1;

        protected Camera m_EventCamera;

        /// <summary>
        /// Layer mask used to filter events. Always combined with the camera's culling mask if a camera is used.
        /// </summary>
        [SerializeField]
        protected LayerMask m_EventMask = kNoEventMaskSet;

        /// <summary>
        /// The max number of intersections allowed. 0 = allocating version anything else is non alloc.
        /// </summary>
        [SerializeField]
        protected int m_MaxRayIntersections = 0;
        protected int m_LastMaxRayIntersections = 0;
        

        protected GTSplineRaycaster()
        {}

        public override Camera eventCamera
        {
            get
            {
                if (m_EventCamera == null)
                    m_EventCamera = GetComponent<Camera>();

                if (m_EventCamera == null)
                    return Camera.main;

                return m_EventCamera ;
            }
        }


        /// <summary>
        /// Depth used to determine the order of event processing.
        /// </summary>
        public virtual int depth
        {
            get { return (eventCamera != null) ? (int)eventCamera.depth : 0xFFFFFF; }
        }

        /// <summary>
        /// Event mask used to determine which objects will receive events.
        /// </summary>
        public int finalEventMask
        {
            get { return (eventCamera != null) ? eventCamera.cullingMask & m_EventMask : kNoEventMaskSet; }
        }

        /// <summary>
        /// Layer mask used to filter events. Always combined with the camera's culling mask if a camera is used.
        /// </summary>
        public LayerMask eventMask
        {
            get { return m_EventMask; }
            set { m_EventMask = value; }
        }

        /// <summary>
        /// Max number of ray intersection allowed to be found.
        /// </summary>
        /// <remarks>
        /// A value of zero will represent using the allocating version of the raycast function where as any other value will use the non allocating version.
        /// </remarks>
        public int maxRayIntersections
        {
            get { return m_MaxRayIntersections; }
            set { m_MaxRayIntersections = value; }
        }

        protected override void Awake()
        {
            base.Awake();
            m_Distances = new NativeList<float>(64, Allocator.Persistent);
            m_NearestPoints = new NativeList<Vector3>(64, Allocator.Persistent);
            m_NearestIndex = new NativeReference<int>(-1,Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            m_RaycastSplinesJobHandle.Complete();
            m_NearestRaycastPointJobHandle.Complete();
            m_Distances.Dispose();
            m_NearestPoints.Dispose();
            m_NearestIndex.Dispose();
        }

        /// <summary>
        /// Returns a ray going from camera through the event position and the distance between the near and far clipping planes along that ray.
        /// </summary>
        /// <param name="eventData">The pointer event for which we will cast a ray.</param>
        /// <param name="ray">The ray to use.</param>
        /// <param name="eventDisplayIndex">The display index used.</param>
        /// <param name="distanceToClipPlane">The distance between the near and far clipping planes along the ray.</param>
        /// <returns>True if the operation was successful. false if it was not possible to compute, such as the eventPosition being outside of the view.</returns>
        protected bool ComputeRayAndDistance(PointerEventData eventData, ref Ray ray, ref int eventDisplayIndex, ref float distanceToClipPlane)
        {
            if (eventCamera == null)
                return false;

            var eventPosition = Display.RelativeMouseAt(eventData.position);
            if (eventPosition != Vector3.zero)
            {
                // We support multiple display and display identification based on event position.
                eventDisplayIndex = (int)eventPosition.z;

                // Discard events that are not part of this display so the user does not interact with multiple displays at once.
                if (eventDisplayIndex != eventCamera.targetDisplay)
                    return false;
            }
            else
            {
                // The multiple display system is not supported on all platforms, when it is not supported the returned position
                // will be all zeros so when the returned index is 0 we will default to the event data to be safe.
                eventPosition = eventData.position;
            }

            // Cull ray casts that are outside of the view rect. (case 636595)
            if (!eventCamera.pixelRect.Contains(eventPosition))
                return false;

            ray = eventCamera.ScreenPointToRay(eventPosition);
            // compensate far plane distance - see MouseEvents.cs
            float projectionDirection = ray.direction.z;
            distanceToClipPlane = Mathf.Approximately(0.0f, projectionDirection)
                ? Mathf.Infinity
                : Mathf.Abs((eventCamera.farClipPlane - eventCamera.nearClipPlane) / projectionDirection);
            return true;
        }

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            Ray ray = new Ray();
            int displayIndex = 0;
            float distanceToClipPlane = 0;
            if (!ComputeRayAndDistance(eventData, ref ray, ref displayIndex, ref distanceToClipPlane))
                return;
            
            Debug.DrawRay(ray.origin, ray.direction, Color.red);

            // You are filling in the result from the last frame
            m_RaycastSplinesJobHandle.Complete();
            m_NearestRaycastPointJobHandle.Complete();
            if (m_NearestIndex.Value != -1)
            {
                var nearestSplineScreenPoint = eventCamera.WorldToScreenPoint(m_NearestPoints[m_NearestIndex.Value]);
                var distance = Vector3.Distance(nearestSplineScreenPoint, eventData.position);
                if (distance < m_SelectScreenDistance)
                {
                    var result = new RaycastResult
                    {
                        gameObject = m_SplinePool.Splines[m_NearestIndex.Value].gameObject,
                        module = this,
                        distance = Vector3.Distance(m_NearestPoints[m_NearestIndex.Value], ray.origin),
                        worldPosition = m_NearestPoints[m_NearestIndex.Value],
                        worldNormal = Vector3.up, // fill in spline normal
                        screenPosition = eventData.position,
                        displayIndex = displayIndex,
                        index = resultAppendList.Count,
                        sortingLayer = 0,
                        sortingOrder = 0
                    };
                    resultAppendList.Add(result);
                }
                
                Debug.DrawRay(m_NearestPoints[m_NearestIndex.Value], Vector3.up * .1f, Color.blue);
            }

            if (m_NearestPoints.Length != m_SplinePool.Splines.Count)
            {
                m_NearestPoints.Length = m_SplinePool.Splines.Count;
                m_Distances.Length = m_SplinePool.Splines.Count;
            }
            
            m_RaycastSplinesJob = new RaycastSplinesJob
            {
                InputRay = ray,
                Splines = m_SplinePool.GetNativeSplinesAndUpdateDirty(),
                NearestPoints = m_NearestPoints,
                Distances = m_Distances
            };
            m_RaycastSplinesJobHandle = m_RaycastSplinesJob.Schedule(m_SplinePool.Splines.Count, 1);

            m_NearestRaycastPointJob = new NearestRaycastPointJob()
            {
                Distances = m_Distances.AsDeferredJobArray(),
                NearestIndex = m_NearestIndex
            };
            m_NearestRaycastPointJobHandle = m_NearestRaycastPointJob.Schedule(m_RaycastSplinesJobHandle);
        }
        
        NativeList<float> m_Distances;
        NativeList<Vector3> m_NearestPoints;
        NativeReference<int> m_NearestIndex;
        RaycastSplinesJob m_RaycastSplinesJob;
        JobHandle m_RaycastSplinesJobHandle;
        NearestRaycastPointJob m_NearestRaycastPointJob;
        JobHandle m_NearestRaycastPointJobHandle;
        
        [BurstCompile]
        struct RaycastSplinesJob : IJobParallelFor
        {
            [ReadOnly]
            public Ray InputRay;

            [ReadOnly]
            public NativeArray<GTUnsafeNativeSpline> Splines;

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
        
        [BurstCompile]
        struct NearestRaycastPointJob : IJob
        {
            [ReadOnly] 
            public NativeArray<float> Distances;

            [WriteOnly] 
            public NativeReference<int> NearestIndex;

            public void Execute()
            {
                float maxDistance = float.MaxValue;
                int nearestIndex = -1;
                for (int i = 0; i < Distances.Length; ++i)
                {
                    if (Distances[i] < maxDistance)
                    {
                        maxDistance = Distances[i];
                        nearestIndex = i;
                    }
                }

                NearestIndex.Value = nearestIndex;
            }
        }
    }
}
