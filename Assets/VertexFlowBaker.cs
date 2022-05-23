using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

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
    NativeQueue<int> m_VertQueue;
    NativeArray<Color> m_FlowColors;
    NativeArray<UnsafeHashSet<int>> m_VertTriangles;
    NativeArray<SubMesh> m_SubMeshes;
    NativeHashSet<int> m_WalkedVertices;
    CalculateOverlappingVertsJob m_CalculateOverlappingVertsJob;
    JobHandle m_CalculateOverlappingVertsJobHandle;
    Mesh.MeshDataArray m_DataArray;
    
    [Serializable]
    public struct SubMesh
    {
        public SubMeshDescriptor Descriptor;
        public UnsafeList<int> Triangles;

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
        public readonly UnsafeList<Vector3> FlowDirections;
        public readonly UnsafeHashSet<int> OverlappingVertIndices;
        public readonly UnsafeHashSet<int> FlowedFromVertIndices;
        public readonly UnsafeHashSet<int> Triangles;

        public FlowBakerVertex(int subMeshIndex, int initialCapacity)
        {
            IsCreated = true;
            SubMeshIndex = subMeshIndex;
            FlowDirections = new UnsafeList<Vector3>(initialCapacity, Allocator.Persistent);
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
        m_VertTriangles = new NativeArray<UnsafeHashSet<int>>(data.vertexCount, Allocator.Persistent);
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
                    m_FlowBakerVertices[vertIndex] = new FlowBakerVertex(submeshIndex, 4);
                m_FlowBakerVertices[vertIndex].Triangles.Add(triIndex);
                unsafeTriangles.Add(vertIndex);
            }
            m_SubMeshes[submeshIndex] = new SubMesh(descriptor, unsafeTriangles);
            triangles.Dispose();
            Debug.Log($"{descriptor.topology} {descriptor.indexCount} {descriptor.vertexCount}");
        }
        
        m_VertQueue = new NativeQueue<int>(Allocator.Persistent);
        
        StartCoroutine(WalkVertices());
    }

    void OnDestroy()
    {
        m_Vertices.Dispose();
        m_Normals.Dispose();
        m_AveragedFlowDirections.Dispose();
        m_WalkedVertices.Dispose();

        for (int i = 0; i < m_FlowBakerVertices.Length; ++i)
            m_FlowBakerVertices[i].Dispose();
        m_FlowBakerVertices.Dispose();
        
        for (int i = 0; i < m_SubMeshes.Length; ++i)
            m_SubMeshes[i].Dispose();

        m_DataArray.Dispose();

        m_VertQueue.Dispose();
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
        [ReadOnly]
        public NativeArray<Vector3> Vertices;
        
        [ReadOnly]
        public NativeArray<int> Indices;
        
        [ReadOnly]
        public NativeArray<SubMesh> SubMeshes;
        
        [ReadOnly]
        public NativeArray<Vector3> FlowedFrom;
        
        [ReadOnly]
        public NativeArray<FlowBakerVertex> FlowBakerVertices;
    
        [ReadOnly] 
        public NativeQueue<int> ReadQueue;
        
        [WriteOnly] 
        public NativeQueue<int> WriteQueue;
        
        [WriteOnly] 
        public NativeHashSet<int> WalkedVertices;
    
        public void Execute()
        {
            UnsafeList<int> foundTriIndices = new UnsafeList<int>( 4, Allocator.Persistent);
            UnsafeHashSet<int> hashIntersection = new UnsafeHashSet<int>(16, Allocator.Persistent);

            while (ReadQueue.Count  > 0)
            {
                int vertIndex = ReadQueue.Dequeue();
                
                foundTriIndices.Clear();
                int subMeshIndex = SearchSubMeshForVert(vertIndex, foundTriIndices);
                
                Vector3 vertPosition = Vertices[vertIndex];
                
                for (int foundTriIndex = 0; foundTriIndex < foundTriIndices.Length; ++foundTriIndex)
                {
                    int triIndex = foundTriIndices[foundTriIndex];
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
                            
                            if (!FlowFromAndOverlapsContain(vertIndex, neighborVertIndex, hashIntersection) && 
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
        
        bool FlowFromAndOverlapsContain(int vertIndex, int neighborIndex, UnsafeHashSet<int> hashIntersection)
        {
            var flowedFrom = FlowBakerVertices[vertIndex].FlowedFromVertIndices;
            if (flowedFrom.Contains(neighborIndex))
            {
                return true;
            }
            
            var neighborFlowedFrom = FlowBakerVertices[neighborIndex].FlowedFromVertIndices;
            hashIntersection.Clear();
            hashIntersection.UnionWith(neighborFlowedFrom);
            hashIntersection.IntersectWith(flowedFrom);
            if (!hashIntersection.IsEmpty)
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
                hashIntersection.Clear();
                hashIntersection.UnionWith(neighborOverlappingFlowedFrom);
                hashIntersection.IntersectWith(flowedFrom);
                if (!hashIntersection.IsEmpty)
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
        
        int SearchSubMeshForVert(int vertIndex, UnsafeList<int> triIndices)
        {
            int subMeshIndex = -1;
            foreach (var triIndex in FlowBakerVertices[vertIndex].Triangles)
            {
                triIndices.Add(triIndex);
                subMeshIndex = FlowBakerVertices[vertIndex].SubMeshIndex;
            }
    
            foreach (var overlapVertIndex in FlowBakerVertices[vertIndex].OverlappingVertIndices)
            {
                foreach (var triIndex in FlowBakerVertices[overlapVertIndex].Triangles)
                {
                    triIndices.Add(triIndex);
                    if (subMeshIndex != FlowBakerVertices[vertIndex].SubMeshIndex)
                        Debug.LogError("Different submeshes on tri vert search? Should never happen.");
                }
            }

            return subMeshIndex;
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
        
        Debug.Log("Calculate Overlap Complete");
        
        for (int i = 0; i < m_StartPoints.Length; ++i)
        {
            int nearestIndex = GetNearestVertex(m_StartPoints[i].transform.position);
            m_VertQueue.Enqueue(nearestIndex);
            AddWalkedVerticesWithOverlaps(nearestIndex);
        }
        
        List<int> foundTriIndices = new();
        NativeList<Vector3> flowDirections = new NativeList<Vector3>(Allocator.Persistent);
        m_RecyclableHashIntersection = new UnsafeHashSet<int>(16, Allocator.Persistent);
        // NativeHashSet<int> overlappingVerts = new NativeHashSet<int>(10, Allocator.Persistent);
        int stepCount = 0;
        int subMeshIndex = -1;
        
        while (m_VertQueue.Count  > 0)
        {
            // while (!Input.GetKeyDown(KeyCode.A))
            // {
            //     Debug.DrawRay(  m_TransformMatrix.MultiplyPoint(m_Vertices[m_VertQueue.Peek()]) , m_TransformMatrix.MultiplyVector(m_Normals[m_VertQueue.Peek()]) * .0002f, Color.magenta);
            //     yield return null;
            // }
            
            int vertIndex = m_VertQueue.Dequeue();
            
            foundTriIndices.Clear();
            int submeshIndex = SearchAllSubMeshesForVert(vertIndex, foundTriIndices);
            
            Vector3 vertPosition = m_Vertices[vertIndex];
            
            for (int foundTriIndex = 0; foundTriIndex < foundTriIndices.Count; ++foundTriIndex)
            {
                int triIndex = foundTriIndices[foundTriIndex];
                int triIndexStartOffset = triIndex % 3;
                int triIndexStart = triIndex - triIndexStartOffset;

                for (int nextTriIndex = 0; nextTriIndex < 3; ++nextTriIndex)
                {
                    int neighborTriIndex = triIndexStart + nextTriIndex;
                    int neighborVertIndex = m_SubMeshes[submeshIndex].Triangles[neighborTriIndex];
                    if (vertIndex != neighborVertIndex && !m_FlowBakerVertices[vertIndex].OverlappingVertIndices.Contains(neighborVertIndex))
                    {
                        if (AddWalkedVerticesWithOverlaps(neighborVertIndex))
                        {
                            m_VertQueue.Enqueue(neighborVertIndex);
                        }
                        
                        if (!FlowFromAndOverlapsContain(vertIndex, neighborVertIndex) && m_FlowBakerVertices[neighborVertIndex].FlowedFromVertIndices.Add(vertIndex))
                        {
                            Vector3 neighborVertPosition = m_Vertices[neighborVertIndex];
                            Vector3 flowDirection = neighborVertPosition - vertPosition;
                            Vector3 normalizedFlowDirection = flowDirection.normalized;
                            m_FlowBakerVertices[neighborVertIndex].FlowDirections.Add(normalizedFlowDirection);
                        }
                    }
                }
            }
            
            stepCount++;
            if (stepCount == 5)
            {
                stepCount = 0;            
                yield return null;
            }
        }
        
        foreach (int walkedVertex in m_WalkedVertices)
        { 
            Vector3 averagedFlow = Vector3.zero;
            int totalFlowCount = 0;

            totalFlowCount += m_FlowBakerVertices[walkedVertex].FlowDirections.Length;
            foreach (Vector3 vector3 in m_FlowBakerVertices[walkedVertex].FlowDirections)
                averagedFlow += vector3;
            
            foreach( var overlappedWalkedVertex in m_FlowBakerVertices[walkedVertex].OverlappingVertIndices)
            {
                totalFlowCount += m_FlowBakerVertices[overlappedWalkedVertex].FlowDirections.Length;
                foreach (Vector3 vector3 in m_FlowBakerVertices[overlappedWalkedVertex].FlowDirections)
                    averagedFlow += vector3;
            }

            if (totalFlowCount > 0)
            {
                averagedFlow /= totalFlowCount;
                m_AveragedFlowDirections[walkedVertex] = averagedFlow;
                
                averagedFlow = averagedFlow * 0.5f + (Vector3.one * 0.5f);
                Color flowColor = new Color(averagedFlow.x, averagedFlow.y, averagedFlow.z, 1);
                m_FlowColors[walkedVertex] = flowColor;
                
                foreach( var i in m_FlowBakerVertices[walkedVertex].OverlappingVertIndices)
                {
                    m_AveragedFlowDirections[i] = m_AveragedFlowDirections[walkedVertex];
                    m_FlowColors[i] = m_FlowColors[walkedVertex];
                }
            }
        }
        
        m_Mesh.colors = m_FlowColors.ToArray();

        flowDirections.Dispose();
        // overlappingVerts.Dispose();
    }

    bool FlowFromAndOverlapsContain(int vertIndex, int neighborIndex)
    {
        // var flowedFrom = m_FlowedFrom[vertIndex];
        var flowedFrom = m_FlowBakerVertices[vertIndex].FlowedFromVertIndices;
        if (flowedFrom.Contains(neighborIndex))
        {
            return true;
        }
        
        // var neighborflowedFrom = m_FlowedFrom[neighborIndex];
        var neighborFlowedFrom = m_FlowBakerVertices[neighborIndex].FlowedFromVertIndices;
        m_RecyclableHashIntersection.Clear();
        m_RecyclableHashIntersection.UnionWith(neighborFlowedFrom);
        m_RecyclableHashIntersection.IntersectWith(flowedFrom);
        if (!m_RecyclableHashIntersection.IsEmpty)
        {
            return true;
        }
        
        foreach (var neighborOverlappingIndex in m_FlowBakerVertices[neighborIndex].OverlappingVertIndices)
        {
            if (flowedFrom.Contains(neighborOverlappingIndex))
            {
                return true;
            }
            
            // var neighborOverlappingFlowedFrom = m_FlowedFrom[neighborOverlappingIndex];
            var neighborOverlappingFlowedFrom = m_FlowBakerVertices[neighborOverlappingIndex].OverlappingVertIndices;
            m_RecyclableHashIntersection.Clear();
            m_RecyclableHashIntersection.UnionWith(neighborOverlappingFlowedFrom);
            m_RecyclableHashIntersection.IntersectWith(flowedFrom);
            if (!m_RecyclableHashIntersection.IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    UnsafeHashSet<int> m_RecyclableHashIntersection;

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

    bool SearchSubMeshForVert(int vertIndex, List<int> triIndices, int subMeshIndex)
    {
        foreach (var triIndex in m_FlowBakerVertices[vertIndex].Triangles)
        {
            triIndices.Add(triIndex);
        }

        foreach (var overlapVertIndex in m_FlowBakerVertices[vertIndex].OverlappingVertIndices)
        {
            foreach (var triIndex in m_FlowBakerVertices[overlapVertIndex].Triangles)
            {
                triIndices.Add(triIndex);
            }
        }
        
        return triIndices.Count > 0;
    }

    int SearchAllSubMeshesForVert(int vertIndex, List<int> triIndices)
    {
        for (int sm = 0; sm < m_SubMeshes.Length; ++sm)
        {
            if (SearchSubMeshForVert(vertIndex, triIndices, sm))
            {
                return sm;
            }
        }

        return -1;
    }

    Color[] m_RandomColors;
    void OnDrawGizmos()
    {
        if (!Application.isPlaying)       
            return;

        if (m_RandomColors == null)
        {
            m_RandomColors = new Color[m_Vertices.Length];
            for (int i = 0; i < m_RandomColors.Length; ++i)
            {
                Color randomColor = new Color(Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f), 1);
                m_RandomColors[i] = randomColor;
            }
        }

        foreach (int walkedVertex in m_WalkedVertices)
        {
            var vertPos = m_TransformMatrix.MultiplyPoint(m_Vertices[walkedVertex]);
            // Gizmos.color = m_RandomColors[walkedVertex];
            // Gizmos.DrawSphere(  vertPos , .0002f);
            // if (m_FlowDirections[walkedVertex] != null)
            // {
            //     for (int i = 0; i < m_FlowDirections[walkedVertex].Count; ++i)
            //     {
            //         Debug.DrawRay(vertPos, m_TransformMatrix.MultiplyVector(m_FlowDirections[walkedVertex][i]) * .001f, new Color(i * 0.1f, i * 0.2f, i * 0.3f, 1));
            //     }
            // }

            foreach (var fromIndex in m_FlowBakerVertices[walkedVertex].FlowedFromVertIndices)
            {
                var fromPos = m_TransformMatrix.MultiplyPoint(m_Vertices[fromIndex]);
                Vector3 ray = (fromPos - vertPos).normalized; 
                Color color = new Color(ray.x, ray.y, ray.z, 1);
                // Debug.DrawRay(vertPos, ray / 2f, m_RandomColors[walkedVertex]);
                Debug.DrawRay(vertPos, ray * .003f, color);
            }

            // Debug.DrawRay(vertPos, m_TransformMatrix.MultiplyVector(m_AveragedFlowDirections[walkedVertex].normalized) * .005f, Color.yellow);
            // Debug.DrawRay(  m_TransformMatrix.MultiplyPoint(m_Vertices[walkedVertex]) , m_TransformMatrix.MultiplyVector(m_Normals[walkedVertex]) * .0001f, Color.green);
        }

        // for (int i = 0; i < m_VertQueue.Count; ++i)
        // {
        //     int vertIndex = m_VertQueue.ToArray()[i];
        //     Gizmos.color = Color.cyan;
        //     var point = m_TransformMatrix.MultiplyPoint(m_Vertices[vertIndex]);
        //     Gizmos.DrawWireCube(  point , Vector3.one * .0002f);
        //     point.y -= .0002f;
        //
        //     Handles.Label(point, vertIndex.ToString());
        //     foreach (var overlapping in m_Overlapping[vertIndex])
        //     {
        //         point.y -= .0002f;
        //         Handles.Label(point, overlapping.ToString());
        //     }
        // }
        
        
        // Debug.DrawRay(  m_TransformMatrix.MultiplyPoint(m_Vertices[m_NearestIndex]) , m_TransformMatrix.MultiplyVector(m_Normals[m_NearestIndex]) * .001f, Color.red);

        // for (int i = 0; i < m_Vertices.Count; ++i)
        // {
        //     Debug.DrawRay(  m_TransformMatrix.MultiplyPoint(m_Vertices[i]) , m_TransformMatrix.MultiplyVector(m_Normals[i]) * .001f, Color.blue);
        // }
    }
}
