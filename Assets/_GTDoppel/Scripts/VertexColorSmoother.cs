using System;
using System.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

public class VertexColorSmoother : MonoBehaviour
{
    [SerializeField] 
    SkinnedMeshRenderer m_Renderer;
    
    [SerializeField] 
    MeshFilter m_MeshFilter;
    
    const float VertexSearchDistance = 0.0000001f;
    
    Mesh m_Mesh;
    NativeArray<Vector3> m_Vertices;
    NativeArray<Vector3> m_Normals;
    NativeArray<BakerVertex> m_BakerVertices;
    NativeArray<Color> m_Colors;
    NativeArray<SubMesh> m_SubMeshes;

    CalculateOverlappingVertsJob m_CalculateOverlappingVertsJob;
    AverageVertexColors m_AverageVertexColors;
    
    JobHandle m_CalculateOverlappingVertsJobHandle;
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
    public struct BakerVertex
    {
        public readonly bool IsCreated;
        public readonly int SubMeshIndex;
        public readonly UnsafeHashSet<int> OverlappingVertIndices;
        public readonly UnsafeHashSet<int> Triangles;

        public BakerVertex(int subMeshIndex, int initialCapacity)
        {
            IsCreated = true;
            SubMeshIndex = subMeshIndex;
            OverlappingVertIndices = new UnsafeHashSet<int>(initialCapacity, Allocator.Persistent);
            Triangles = new UnsafeHashSet<int>(initialCapacity, Allocator.Persistent);
        }

        public void Dispose()
        {
            OverlappingVertIndices.Dispose();
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
        
        public NativeArray<BakerVertex> FlowBakerVertices;

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

    [ContextMenu("Run")]
    void Run()
    {
        StartCoroutine(RunCoroutine());
    }

    IEnumerator RunCoroutine()
    {
        Initialize();
        yield return AverageColors();
        Cleanup();
    }

    void Initialize()
    {
        m_Mesh = m_MeshFilter.sharedMesh;
        
        m_DataArray = Mesh.AcquireReadOnlyMeshData(m_Mesh);
        var data = m_DataArray[0];

        m_Vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Persistent);  
        data.GetVertices(m_Vertices);
        m_Normals = new NativeArray<Vector3>(data.vertexCount, Allocator.Persistent);  
        data.GetNormals(m_Normals);
        m_Colors = new NativeArray<Color>(data.vertexCount, Allocator.Persistent);
        data.GetColors(m_Colors);
        
        m_BakerVertices = new NativeArray<BakerVertex>(data.vertexCount, Allocator.Persistent);
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
                if (!m_BakerVertices[vertIndex].IsCreated)
                {
                    m_BakerVertices[vertIndex] = new BakerVertex(submeshIndex, 4);
                }

                // can i convert NativeArray to unsafelist directly somehow?
                m_BakerVertices[vertIndex].Triangles.Add(triIndex);
                unsafeTriangles.Add(vertIndex);
            }
            m_SubMeshes[submeshIndex] = new SubMesh(descriptor, unsafeTriangles);
            triangles.Dispose();
        }
    }

    void Cleanup()
    {
        m_Vertices.Dispose();
        m_Normals.Dispose();
        m_Colors.Dispose();

        for (int i = 0; i < m_BakerVertices.Length; ++i)
            m_BakerVertices[i].Dispose();
        m_BakerVertices.Dispose();
        
        for (int i = 0; i < m_SubMeshes.Length; ++i)
            m_SubMeshes[i].Dispose();

        m_SubMeshes.Dispose();
        m_DataArray.Dispose();
    }

    [BurstCompile]
    struct AverageVertexColors : IJob
    {
        public NativeArray<BakerVertex> BakerVertices;
        
        public NativeArray<Color> Colors;
        
        [ReadOnly]
        public NativeArray<SubMesh> SubMeshes;
        
        public void Execute()
        {
            for (int vertIndex = 0; vertIndex < BakerVertices.Length; ++vertIndex)
            { 
                Color averagedColor = Colors[vertIndex];
                int totalCount = 1;

                AddVertColors(vertIndex, ref averagedColor, ref totalCount);
            
                if (totalCount > 0)
                {
                    averagedColor /= totalCount;
                    Colors[vertIndex] = averagedColor;
                    
                    foreach( var overlappedIndex in BakerVertices[vertIndex].OverlappingVertIndices)
                    {
                        Colors[overlappedIndex] = averagedColor;
                    }
                }
            }
        }

        void AddVertColors(int vertIndex, ref Color averagedColor, ref int totalFlowCount)
        {
            foreach (var triIndex in BakerVertices[vertIndex].Triangles)
            {
                AverageTriIndexColors(vertIndex, triIndex, BakerVertices[vertIndex].SubMeshIndex, ref averagedColor, ref totalFlowCount);
            }
    
            foreach (var overlapVertIndex in BakerVertices[vertIndex].OverlappingVertIndices)
            {
                foreach (var triIndex in BakerVertices[overlapVertIndex].Triangles)
                {
                    AverageTriIndexColors(vertIndex, triIndex, BakerVertices[overlapVertIndex].SubMeshIndex, ref averagedColor, ref totalFlowCount);
                }
            }
        }

        void AverageTriIndexColors(int vertIndex, int triIndex, int subMeshIndex, ref Color averagedFlow, ref int totalFlowCount)
        {
            int triIndexStartOffset = triIndex % 3;
            int triIndexStart = triIndex - triIndexStartOffset;

            for (int nextTriIndex = 0; nextTriIndex < 3; ++nextTriIndex)
            {
                int neighborTriIndex = triIndexStart + nextTriIndex;
                int neighborVertIndex = SubMeshes[subMeshIndex].Triangles[neighborTriIndex];
                if (vertIndex != neighborVertIndex && !BakerVertices[vertIndex].OverlappingVertIndices.Contains(neighborVertIndex))
                {
                    averagedFlow += Colors[neighborVertIndex];
                    totalFlowCount++;
                }
            }
        }
    }
    
    IEnumerator AverageColors()
    {
        m_CalculateOverlappingVertsJob = new CalculateOverlappingVertsJob
        {
            SearchDistance = VertexSearchDistance,
            Vertices = m_Vertices,
            FlowBakerVertices = m_BakerVertices,
        };
        m_CalculateOverlappingVertsJobHandle = m_CalculateOverlappingVertsJob.Schedule(m_Vertices.Length, 4);
        yield return new WaitUntil(() => m_CalculateOverlappingVertsJobHandle.IsCompleted);
        m_CalculateOverlappingVertsJobHandle.Complete();
        
        m_AverageVertexColors = new AverageVertexColors
        {
            BakerVertices = m_BakerVertices,
            Colors = m_Colors,
            SubMeshes = m_SubMeshes
        };
        m_AverageDirectionsJobHandle = m_AverageVertexColors.Schedule();
        yield return new WaitUntil(() => m_AverageDirectionsJobHandle.IsCompleted);
        m_AverageDirectionsJobHandle.Complete();

        m_Mesh.colors = m_Colors.ToArray();
        m_Mesh.UploadMeshData(false);
    }
}
