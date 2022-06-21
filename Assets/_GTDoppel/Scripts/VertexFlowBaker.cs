using System;
using System.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

public class VertexFlowBaker : MonoBehaviour
{
    [SerializeField] 
    SkinnedMeshRenderer m_Renderer;

    [SerializeField] 
    MeshFilter m_DisplayMeshFilter;
    
    [SerializeField] 
    Transform m_Armature;

    [SerializeField] 
    Transform[] m_StartPoints;

    const float VertexSearchDistance = 0.0000001f;

    Matrix4x4 m_TransformMatrix;

    Mesh m_Mesh;
    NativeArray<Vector3> m_Vertices;
    NativeArray<Vector3> m_Normals;
    NativeArray<Vector3> m_AveragedFlowDirections;
    NativeArray<FlowBakerVertex> m_FlowBakerVertices;
    NativeQueue<int> m_ReadVertQueue;
    NativeQueue<int> m_WriteVertQueue;
    NativeArray<Color> m_FlowColors;
    NativeArray<SubMesh> m_SubMeshes;
    NativeHashSet<int> m_WalkedVertices;
    NativeList<int> m_FoundTriIndices;
    NativeHashSet<int> m_HashIntersection;
    CalculateOverlappingVertsJob m_CalculateOverlappingVertsJob;
    MeshWalkJob m_MeshWalkJob;
    CalculateAverageDirections m_CalculateAverageDirectionsFlowJob;
    AverageFlowDirectionsJob m_AverageFlowDirectionsJob;
    JobHandle m_CalculateOverlappingVertsJobHandle;
    JobHandle m_MeshWalkJobHandle;
    JobHandle m_CalculateAverageDirectionsFlowJobHandle;
    JobHandle m_AverageDirectionsJobHandle;
    Mesh.MeshDataArray m_DataArray;
    
    [Serializable]
    public struct SubMesh
    {
        public readonly SubMeshDescriptor Descriptor;
        public readonly UnsafeList<int> Triangles;

        public SubMesh(SubMeshDescriptor descriptor, UnsafeList<int> triangles)
        {
            Descriptor = descriptor;
            Triangles = triangles;
        }

        public void Dispose()
        {
            Triangles.Dispose();
        }
    }
    
    [Serializable]
    public struct FlowBakerVertex
    {
        public readonly bool IsCreated;
        public readonly int SubMeshIndex;
        public readonly UnsafeHashSet<Vector3> FlowDirections;
        public readonly UnsafeHashSet<int> OverlappingVertIndices;
        public readonly UnsafeHashSet<int> FlowedFromVertIndices;
        public readonly UnsafeHashSet<int> Triangles;

        public FlowBakerVertex(int subMeshIndex, int initialCapacity)
        {
            IsCreated = true;
            SubMeshIndex = subMeshIndex;
            FlowDirections = new UnsafeHashSet<Vector3>(initialCapacity, Allocator.Persistent);
            OverlappingVertIndices = new UnsafeHashSet<int>(initialCapacity, Allocator.Persistent);
            FlowedFromVertIndices = new UnsafeHashSet<int>(initialCapacity, Allocator.Persistent);
            Triangles = new UnsafeHashSet<int>(initialCapacity, Allocator.Persistent);
        }

        public void Dispose()
        {
            FlowDirections.Dispose();
            OverlappingVertIndices.Dispose();
            FlowedFromVertIndices.Dispose();
            Triangles.Dispose();
        }
    }

    [BurstCompile]
    struct CalculateOverlappingVertsJob : IJobParallelFor
    {
        [ReadOnly]
        public float SearchDistance;
        
        [ReadOnly]
        public NativeArray<Vector3> Vertices;
        
        public NativeArray<FlowBakerVertex> FlowBakerVertices;

        public void Execute(int vertIndex)
        {
            for (int searchIndex = 0; searchIndex < Vertices.Length; ++searchIndex)
            {
                if ((Vertices[vertIndex] - Vertices[searchIndex]).sqrMagnitude < SearchDistance && searchIndex != vertIndex)
                {
                    FlowBakerVertices[vertIndex].OverlappingVertIndices.Add(searchIndex);
                }
            }
        }
    }

    void Start()
    {
        m_TransformMatrix = m_Renderer.localToWorldMatrix;
        
        m_Mesh = new Mesh();
        m_Renderer.BakeMesh(m_Mesh);
        m_DisplayMeshFilter.sharedMesh = m_Mesh;
        
        m_DataArray = Mesh.AcquireReadOnlyMeshData(m_Mesh);
        var data = m_DataArray[0];

        m_Vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Persistent);  
        data.GetVertices(m_Vertices);
        m_Normals = new NativeArray<Vector3>(data.vertexCount, Allocator.Persistent);  
        data.GetNormals(m_Normals);

        m_FlowColors = new NativeArray<Color>(data.vertexCount, Allocator.Persistent); 
        m_AveragedFlowDirections = new NativeArray<Vector3>(data.vertexCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        m_FlowBakerVertices = new NativeArray<FlowBakerVertex>(data.vertexCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        m_WalkedVertices = new NativeHashSet<int>(data.vertexCount, Allocator.Persistent);
        m_SubMeshes = new NativeArray<SubMesh>(data.subMeshCount, Allocator.Persistent);
        for (int submeshIndex = 0; submeshIndex < data.subMeshCount; ++submeshIndex)
        {
            var descriptor = m_Mesh.GetSubMesh(submeshIndex);
            var triangles = new NativeArray<int>(descriptor.indexCount, Allocator.Persistent);
            data.GetIndices(triangles, submeshIndex);
            UnsafeList<int> unsafeTriangles = new UnsafeList<int>(triangles.Length, Allocator.Persistent);
            for (int triIndex = 0; triIndex < triangles.Length; ++triIndex)
            {
                int vertIndex = triangles[triIndex];
                if (!m_FlowBakerVertices[vertIndex].IsCreated)
                {
                    m_FlowBakerVertices[vertIndex] = new FlowBakerVertex(submeshIndex, 4);
                    m_FlowColors[vertIndex] = Color.magenta;
                }

                // can i convert NativeArray to unsafelist directly somehow?
                m_FlowBakerVertices[vertIndex].Triangles.Add(triIndex);
                unsafeTriangles.Add(vertIndex);
            }
            m_SubMeshes[submeshIndex] = new SubMesh(descriptor, unsafeTriangles);
            triangles.Dispose();
            Debug.Log($"{descriptor.topology} {descriptor.indexCount} {descriptor.vertexCount}");
        }
        
        m_ReadVertQueue = new NativeQueue<int>(Allocator.Persistent);
        m_WriteVertQueue = new NativeQueue<int>(Allocator.Persistent);
        m_FoundTriIndices = new NativeList<int>( 4, Allocator.Persistent);
        m_HashIntersection = new NativeHashSet<int>(16, Allocator.Persistent);
        
        StartCoroutine(WalkVertices());
    }

    void OnDestroy()
    {
        m_Vertices.Dispose();
        m_Normals.Dispose();
        m_AveragedFlowDirections.Dispose();
        m_WalkedVertices.Dispose();
        m_FlowColors.Dispose();

        for (int i = 0; i < m_FlowBakerVertices.Length; ++i)
            m_FlowBakerVertices[i].Dispose();
        m_FlowBakerVertices.Dispose();
        
        for (int i = 0; i < m_SubMeshes.Length; ++i)
            m_SubMeshes[i].Dispose();

        m_SubMeshes.Dispose();
        m_DataArray.Dispose();
        m_ReadVertQueue.Dispose();
        m_WriteVertQueue.Dispose();
        m_FoundTriIndices.Dispose();
        m_HashIntersection.Dispose();
    }

    int GetNearestVertex(Vector3 fromPoint)
    {
        int nearestIndex = -1;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < m_Vertices.Length; ++i)
        {
            var point = m_TransformMatrix.MultiplyPoint(m_Vertices[i]);
            var distance = (point - fromPoint).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }
    
    [BurstCompile]
    struct MeshWalkJob : IJob
    {
        public NativeQueue<int> ReadQueue;

        public NativeList<int> FoundTriIndices;
        
        public NativeHashSet<int> HashIntersection;

        public NativeArray<FlowBakerVertex> FlowBakerVertices;
        
        [ReadOnly]
        public NativeArray<Vector3> Vertices;

        [ReadOnly]
        public NativeArray<SubMesh> SubMeshes;
        
        [WriteOnly] 
        public NativeQueue<int> WriteQueue;
        
        [WriteOnly] 
        public NativeHashSet<int> WalkedVertices;
        
        public void Execute()
        {
            while (ReadQueue.Count  > 0)
            {
                int vertIndex = ReadQueue.Dequeue();
                
                FoundTriIndices.Clear();
                int subMeshIndex = SearchSubMeshForVert(vertIndex);
                
                Vector3 vertPosition = Vertices[vertIndex];
                
                for (int foundTriIndex = 0; foundTriIndex < FoundTriIndices.Length; ++foundTriIndex)
                {
                    int triIndex = FoundTriIndices[foundTriIndex];
                    int triIndexStartOffset = triIndex % 3;
                    int triIndexStart = triIndex - triIndexStartOffset;
    
                    for (int nextTriIndex = 0; nextTriIndex < 3; ++nextTriIndex)
                    {
                        int neighborTriIndex = triIndexStart + nextTriIndex;
                        int neighborVertIndex = SubMeshes[subMeshIndex].Triangles[neighborTriIndex];
                        if (vertIndex != neighborVertIndex && !FlowBakerVertices[vertIndex].OverlappingVertIndices.Contains(neighborVertIndex))
                        {
                            if (AddWalkedVerticesWithOverlaps(neighborVertIndex))
                            {
                                WriteQueue.Enqueue(neighborVertIndex);
                            }
                            
                            if (!FlowFromAndOverlapsContain(vertIndex, neighborVertIndex) && 
                                FlowBakerVertices[neighborVertIndex].FlowedFromVertIndices.Add(vertIndex))
                            {
                                Vector3 neighborVertPosition = Vertices[neighborVertIndex];
                                Vector3 flowDirection = neighborVertPosition - vertPosition;
                                Vector3 normalizedFlowDirection = flowDirection.normalized;
                                FlowBakerVertices[neighborVertIndex].FlowDirections.Add(normalizedFlowDirection);
                            }
                        }
                    }
                }
            }
        }
        
        bool FlowFromAndOverlapsContain(int vertIndex, int neighborIndex)
        {
            var flowedFrom = FlowBakerVertices[vertIndex].FlowedFromVertIndices;
            if (flowedFrom.Contains(neighborIndex))
            {
                return true;
            }
            
            var neighborFlowedFrom = FlowBakerVertices[neighborIndex].FlowedFromVertIndices;
            HashIntersection.Clear();
            HashIntersection.UnionWith(neighborFlowedFrom);
            HashIntersection.IntersectWith(flowedFrom);
            if (!HashIntersection.IsEmpty)
            {
                return true;
            }
        
            foreach (var neighborOverlappingIndex in FlowBakerVertices[neighborIndex].OverlappingVertIndices)
            {
                if (flowedFrom.Contains(neighborOverlappingIndex))
                {
                    return true;
                }
                
                var neighborOverlappingFlowedFrom = FlowBakerVertices[neighborOverlappingIndex].OverlappingVertIndices;
                HashIntersection.Clear();
                HashIntersection.UnionWith(neighborOverlappingFlowedFrom);
                HashIntersection.IntersectWith(flowedFrom);
                if (!HashIntersection.IsEmpty)
                {
                    return true;
                }
            }

            return false;
        }
        
        bool AddWalkedVerticesWithOverlaps(int index)
        {
            if (WalkedVertices.Add(index))
            {
                foreach (int i in FlowBakerVertices[index].OverlappingVertIndices)
                    WalkedVertices.Add(i);

                return true;
            }
            return false;
        }
        
        int SearchSubMeshForVert(int vertIndex)
        {
            int subMeshIndex = -1;
            foreach (var triIndex in FlowBakerVertices[vertIndex].Triangles)
            {
                FoundTriIndices.Add(triIndex);
                subMeshIndex = FlowBakerVertices[vertIndex].SubMeshIndex;
            }
    
            foreach (var overlapVertIndex in FlowBakerVertices[vertIndex].OverlappingVertIndices)
            {
                foreach (var triIndex in FlowBakerVertices[overlapVertIndex].Triangles)
                {
                    FoundTriIndices.Add(triIndex);
                    if (subMeshIndex != FlowBakerVertices[vertIndex].SubMeshIndex)
                        Debug.LogError("Different submeshes on tri vert search? Should never happen.");
                }
            }

            return subMeshIndex;
        }
    }
    
    [BurstCompile]
    struct CalculateAverageDirections : IJob
    {
        [ReadOnly]
        public int FlowStepCount;
        
        [ReadOnly]
        public NativeArray<FlowBakerVertex> FlowBakerVertices;
        
        [WriteOnly]
        public NativeArray<Vector3> AveragedFlowDirections;
        
        [WriteOnly]
        public NativeArray<Color> FlowColors;
        
        public void Execute()
        {
            for (int vertIndex = 0; vertIndex < FlowBakerVertices.Length; ++vertIndex)
            { 
                Vector3 averagedFlow = Vector3.zero;
                int totalFlowCount = 1;
                int flowFromStepCount = FlowStepCount;
            
                AddFlowDirectionsToAverage(vertIndex, ref averagedFlow, ref totalFlowCount, flowFromStepCount);
            
                if (totalFlowCount > 0)
                {
                    averagedFlow /= totalFlowCount;
                    AveragedFlowDirections[vertIndex] = averagedFlow;
                
                    Vector3 halvedAverageFlow = averagedFlow * 0.5f + (Vector3.one * 0.5f);
                    Color flowColor = new Color(halvedAverageFlow.x, halvedAverageFlow.y, halvedAverageFlow.z, 1);
                    FlowColors[vertIndex] = flowColor;
                
                    foreach( var overlappedIndex in FlowBakerVertices[vertIndex].OverlappingVertIndices)
                    {
                        AveragedFlowDirections[overlappedIndex] = averagedFlow;
                        FlowColors[overlappedIndex] = flowColor;
                    }
                }
            }
        }
        
        void AddFlowDirectionsToAverage(int vertIndex, ref Vector3 averagedFlow, ref int totalFlowCount, int flowedFromStepCount)
        {
            totalFlowCount += FlowBakerVertices[vertIndex].FlowDirections.Count();
            foreach (Vector3 vector3 in FlowBakerVertices[vertIndex].FlowDirections)
                averagedFlow += vector3;
            
            foreach( var overlappedWalkedVertex in FlowBakerVertices[vertIndex].OverlappingVertIndices)
            {
                totalFlowCount += FlowBakerVertices[overlappedWalkedVertex].FlowDirections.Count();
                foreach (Vector3 vector3 in FlowBakerVertices[overlappedWalkedVertex].FlowDirections)
                    averagedFlow += vector3;
            }

            flowedFromStepCount--;
            if (flowedFromStepCount > 0)
            {
                foreach (var flowedFromVertex in FlowBakerVertices[vertIndex].FlowedFromVertIndices)
                {
                    AddFlowDirectionsToAverage(flowedFromVertex, ref averagedFlow, ref totalFlowCount, flowedFromStepCount);
                }
            }
        }
    }
    
    [BurstCompile]
    struct AverageFlowDirectionsJob : IJob
    {
        public NativeArray<FlowBakerVertex> FlowBakerVertices;
        
        public NativeArray<Vector3> AveragedFlowDirections;
        
        [WriteOnly]
        public NativeArray<Color> FlowColors;
        
        [ReadOnly]
        public NativeArray<SubMesh> SubMeshes;
        
        public void Execute()
        {
            for (int vertIndex = 0; vertIndex < FlowBakerVertices.Length; ++vertIndex)
            { 
                Vector3 averagedFlow = AveragedFlowDirections[vertIndex];
                int totalFlowCount = 1;
                int flowFromStepCount = 5;
            
                AddVertColors(vertIndex, ref averagedFlow, ref totalFlowCount);
            
                if (totalFlowCount > 0)
                {
                    averagedFlow /= totalFlowCount;
                    AveragedFlowDirections[vertIndex] = averagedFlow;
                
                    Vector3 halvedAverageFlow = averagedFlow * 0.5f + (Vector3.one * 0.5f);
                    Color flowColor = new Color(halvedAverageFlow.x, halvedAverageFlow.y, halvedAverageFlow.z, 1);
                    FlowColors[vertIndex] = flowColor;
                
                    foreach( var overlappedIndex in FlowBakerVertices[vertIndex].OverlappingVertIndices)
                    {
                        AveragedFlowDirections[overlappedIndex] = averagedFlow;
                        FlowColors[overlappedIndex] = flowColor;
                    }
                }
            }
        }

        void AddVertColors(int vertIndex, ref Vector3 averagedFlow, ref int totalFlowCount)
        {
            foreach (var triIndex in FlowBakerVertices[vertIndex].Triangles)
            {
                AverageTriIndexColors(vertIndex, triIndex, FlowBakerVertices[vertIndex].SubMeshIndex, ref averagedFlow, ref totalFlowCount);
            }
    
            foreach (var overlapVertIndex in FlowBakerVertices[vertIndex].OverlappingVertIndices)
            {
                foreach (var triIndex in FlowBakerVertices[overlapVertIndex].Triangles)
                {
                    AverageTriIndexColors(vertIndex, triIndex, FlowBakerVertices[overlapVertIndex].SubMeshIndex, ref averagedFlow, ref totalFlowCount);
                }
            }
        }

        void AverageTriIndexColors(int vertIndex, int triIndex, int subMeshIndex, ref Vector3 averagedFlow, ref int totalFlowCount)
        {
            int triIndexStartOffset = triIndex % 3;
            int triIndexStart = triIndex - triIndexStartOffset;

            for (int nextTriIndex = 0; nextTriIndex < 3; ++nextTriIndex)
            {
                int neighborTriIndex = triIndexStart + nextTriIndex;
                int neighborVertIndex = SubMeshes[subMeshIndex].Triangles[neighborTriIndex];
                if (vertIndex != neighborVertIndex && !FlowBakerVertices[vertIndex].OverlappingVertIndices.Contains(neighborVertIndex))
                {
                    averagedFlow += AveragedFlowDirections[neighborVertIndex];
                    totalFlowCount++;
                    // foreach (var flowDirection in FlowBakerVertices[neighborVertIndex].FlowDirections)
                    // {
                    //     averagedFlow += flowDirection;
                    //     totalFlowCount++;
                    // }
                }
            }
        }
    }
    
    IEnumerator WalkVertices()
    {
        m_CalculateOverlappingVertsJob = new CalculateOverlappingVertsJob
        {
            SearchDistance = VertexSearchDistance,
            Vertices = m_Vertices,
            FlowBakerVertices = m_FlowBakerVertices,
        };
        m_CalculateOverlappingVertsJobHandle = m_CalculateOverlappingVertsJob.Schedule(m_Vertices.Length, 4);
        yield return new WaitUntil(() => m_CalculateOverlappingVertsJobHandle.IsCompleted);
        m_CalculateOverlappingVertsJobHandle.Complete();
        
        Debug.Log("Calculate Overlap Complete");
        
        for (int i = 0; i < m_StartPoints.Length; ++i)
        {
            if (!m_StartPoints[i].gameObject.activeSelf)
                continue;
                
            int nearestIndex = GetNearestVertex(m_StartPoints[i].transform.position);
            m_ReadVertQueue.Enqueue(nearestIndex);
            AddWalkedVerticesWithOverlaps(nearestIndex);
        }

        Debug.Log($"Added {m_StartPoints.Length}. Starting Mesh Walk.");

        while (m_ReadVertQueue.Count > 0)
        {
            Debug.Log($"Walking from {m_ReadVertQueue.Count} vertices.");
            m_MeshWalkJob = new MeshWalkJob
            {
                FoundTriIndices = m_FoundTriIndices,
                HashIntersection = m_HashIntersection,
                Vertices = m_Vertices,
                SubMeshes = m_SubMeshes,
                FlowBakerVertices = m_FlowBakerVertices,
                ReadQueue = m_ReadVertQueue,
                WriteQueue = m_WriteVertQueue,
                WalkedVertices = m_WalkedVertices
            };
            m_MeshWalkJobHandle = m_MeshWalkJob.Schedule();
            yield return new WaitUntil(() => m_MeshWalkJobHandle.IsCompleted);
            m_MeshWalkJobHandle.Complete();

            Debug.Log($"Walked and added {m_WriteVertQueue.Count} vertices.");

            DrawDebugFlowRays();
            
            while (m_WriteVertQueue.Count > 0)
            {
                m_ReadVertQueue.Enqueue(m_WriteVertQueue.Dequeue());
            }
        }

        // smooth from flow
        Debug.Log($"Smooth From Flow");
        m_CalculateAverageDirectionsFlowJob = new CalculateAverageDirections
        {
            FlowStepCount = 5,
            FlowBakerVertices = m_FlowBakerVertices,
            AveragedFlowDirections = m_AveragedFlowDirections,
            FlowColors = m_FlowColors
        };
        m_CalculateAverageDirectionsFlowJobHandle = m_CalculateAverageDirectionsFlowJob.Schedule();
        yield return new WaitUntil(() => m_CalculateAverageDirectionsFlowJobHandle.IsCompleted);
        m_CalculateAverageDirectionsFlowJobHandle.Complete();
        
        // m_Mesh.colors = m_FlowColors.ToArray();
        // m_Mesh.UploadMeshData(false);
        //
        // // smooth 4 times
        // const int SmoothIterationCount = 1;
        // for (int i = 0; i < SmoothIterationCount; ++i)
        // {
        //     Debug.Log($"Smooth Iteration {i}");
        //     yield return StartCoroutine(ApplyDirectionSmooth());
        //     
        //     m_Mesh.colors = m_FlowColors.ToArray();
        //     m_Mesh.UploadMeshData(false);
        // }
        
        m_Mesh.colors = m_FlowColors.ToArray();
        m_Mesh.UploadMeshData(false);

        while (true)
        {
            DrawDebugFlowRays();
            yield return null;
        }
    }

    IEnumerator ApplyDirectionSmooth()
    {
        m_AverageFlowDirectionsJob = new AverageFlowDirectionsJob
        {
            FlowBakerVertices = m_FlowBakerVertices,
            AveragedFlowDirections = m_AveragedFlowDirections,
            FlowColors = m_FlowColors,
            SubMeshes = m_SubMeshes
        };
        m_AverageDirectionsJobHandle = m_AverageFlowDirectionsJob.Schedule();
        yield return new WaitUntil(() => m_AverageDirectionsJobHandle.IsCompleted);
        m_AverageDirectionsJobHandle.Complete();
    }
    
    bool AddWalkedVerticesWithOverlaps(int index)
    {
        if (m_WalkedVertices.Add(index))
        {
            foreach (int i in m_FlowBakerVertices[index].OverlappingVertIndices)
                m_WalkedVertices.Add(i);

            return true;
        }
        return false;
    }
    
    void DrawDebugFlowRays()
    {
        foreach (int walkedVertex in m_WalkedVertices)
        {
            var vertPos = m_TransformMatrix.MultiplyPoint(m_Vertices[walkedVertex]);
            foreach (var fromIndex in m_FlowBakerVertices[walkedVertex].FlowedFromVertIndices)
            {
                var fromPos = m_TransformMatrix.MultiplyPoint(m_Vertices[fromIndex]);
                Vector3 ray = (fromPos - vertPos).normalized; 
                // Color color = new Color(ray.x, ray.y, ray.z, 1);
                Debug.DrawRay(vertPos, ray * .006f, m_FlowColors[walkedVertex]);
                Debug.DrawRay(vertPos,  m_TransformMatrix.MultiplyVector(m_AveragedFlowDirections[walkedVertex]) * .01f, m_FlowColors[walkedVertex]);
                
            }
        }
    }
}
