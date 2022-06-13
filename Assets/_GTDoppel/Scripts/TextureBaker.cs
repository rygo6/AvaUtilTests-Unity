using System;
using System.Collections;
using GeoTetra.GTSplines;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

public class TextureBaker : MonoBehaviour
{
    [SerializeField] 
    GTSplinePool m_SplinePool;
    
    [SerializeField] 
    Vector2Int m_Resolution = new Vector2Int(512, 512);
    
    [SerializeField] 
    SkinnedMeshRenderer m_Renderer;

    [SerializeField] 
    MeshFilter m_DisplayMeshFilter;
    
    [SerializeField] 
    int m_TrianglePixelDilate = 2;

    [SerializeField] 
    ComputeShader m_ComputeShader;

    Texture2D m_Texture2D;
    RenderTexture m_RenderTexture;

    const float VertexSearchDistance = 0.0000001f;

    Matrix4x4 m_TransformMatrix;

    Mesh m_Mesh;
    NativeArray<Vector3> m_Vertices;
    NativeArray<Vector2> m_UVs;
    NativeArray<Vector3> m_Normals;
    NativeArray<VertexData> m_VertexDatums;
    NativeArray<SubMesh> m_SubMeshes;

    Mesh.MeshDataArray m_DataArray;

    int m_BakeSplineKernel;
    ComputeBuffer m_VerticesBuffer;
    ComputeBuffer m_uvsBuffer;
    ComputeBuffer m_trisBuffer;

    SubMeshDescriptor m_SelectedSubMesh;
    
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
    public struct VertexData
    {
        public readonly bool IsCreated;
        public readonly int SubMeshIndex;
        public readonly UnsafeHashSet<int> OverlappingVertIndices;
        public readonly UnsafeHashSet<int> Triangles;

        public VertexData(int subMeshIndex, int initialCapacity)
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
        
        public NativeArray<VertexData> FlowBakerVertices;

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
        // m_DisplayMeshFilter.sharedMesh = m_Mesh;
        
        m_DataArray = Mesh.AcquireReadOnlyMeshData(m_Mesh);
        var data = m_DataArray[0];

        m_Vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Persistent);  
        data.GetVertices(m_Vertices);
        m_Normals = new NativeArray<Vector3>(data.vertexCount, Allocator.Persistent);  
        data.GetNormals(m_Normals);
        m_UVs = new NativeArray<Vector2>(data.vertexCount, Allocator.Persistent);  
        data.GetUVs(0, m_UVs);
        
        m_VertexDatums = new NativeArray<VertexData>(data.vertexCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
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
                if (!m_VertexDatums[vertIndex].IsCreated)
                {
                    m_VertexDatums[vertIndex] = new VertexData(submeshIndex, 4);
                }

                // can i convert NativeArray to unsafelist directly somehow?
                m_VertexDatums[vertIndex].Triangles.Add(triIndex);
                unsafeTriangles.Add(vertIndex);
            }
            m_SubMeshes[submeshIndex] = new SubMesh(descriptor, unsafeTriangles);
            triangles.Dispose();
            Debug.Log($"{descriptor.topology} {descriptor.indexCount} {descriptor.vertexCount}");
        }

        m_Texture2D = new Texture2D(m_Resolution.x, m_Resolution.y, TextureFormat.RGB24, 0, false);
        m_Texture2D.Apply();
        
        m_RenderTexture = new RenderTexture(m_Resolution.x, m_Resolution.y, 24)
        {
            enableRandomWrite = true
        };
        m_Renderer.material.mainTexture = m_RenderTexture;



        m_BakeSplineKernel = m_ComputeShader.FindKernel("BakeSpline");
        m_ComputeShader.GetKernelThreadGroupSizes(m_BakeSplineKernel, out uint threadSize, out _, out _);
        int triStride = 63;
        Debug.Log(triStride);
        
        m_VerticesBuffer = new ComputeBuffer(m_Vertices.Length, sizeof(float) * 3);
        m_VerticesBuffer.SetData(m_Vertices);
        m_ComputeShader.SetBuffer(m_BakeSplineKernel, "vertices", m_VerticesBuffer);
        m_ComputeShader.SetInt( "verticesCount", m_Vertices.Length);

        m_uvsBuffer = new ComputeBuffer(m_UVs.Length, sizeof(float) * 2);
        m_uvsBuffer.SetData(m_UVs);
        m_ComputeShader.SetBuffer(m_BakeSplineKernel, "uvs", m_uvsBuffer);
        
        // m_trisBuffer.SetData(m_SubMeshes[0].Triangles);  MAKE THIS 
        m_SelectedSubMesh = m_Mesh.GetSubMesh(0);
        var tris = new NativeArray<int>(m_SelectedSubMesh.indexCount, Allocator.Persistent);
        data.GetIndices(tris, 0);
        m_trisBuffer = new ComputeBuffer(tris.Length, sizeof(int));
        m_trisBuffer.SetData(tris);
        m_ComputeShader.SetBuffer(m_BakeSplineKernel, "tris", m_trisBuffer);
        m_ComputeShader.SetInt( "trisCount", tris.Length);
        m_ComputeShader.SetInt( "trisStride", triStride);
        m_ComputeShader.SetInt( "trianglePixelDilate", m_TrianglePixelDilate);
        m_ComputeShader.SetInts( "resolution", m_Resolution.x, m_Resolution.y);
        
        m_ComputeShader.SetTexture(m_BakeSplineKernel, "bakedTexture", m_RenderTexture);
        

        // StartCoroutine(ProcessComputerShader());
    }

    void Update()
    {
        int triStride = 63;
        m_ComputeShader.Dispatch(m_BakeSplineKernel, m_SelectedSubMesh.indexCount /  triStride, 1, 1);
    }

    void OnDestroy()
    {
        m_Vertices.Dispose();
        m_Normals.Dispose();
        m_UVs.Dispose();

        for (int i = 0; i < m_VertexDatums.Length; ++i)
            m_VertexDatums[i].Dispose();
        m_VertexDatums.Dispose();
        
        for (int i = 0; i < m_SubMeshes.Length; ++i)
            m_SubMeshes[i].Dispose();

        m_SubMeshes.Dispose();
        m_DataArray.Dispose();
    }

    bool InTriangle(Vector3 A,Vector3 B, Vector3 C, Vector3 P)
    {
        Vector3 v0 = C - A;
        Vector3 v1 = B - A;
        Vector3 v2 = P - A;
        
        float dot00 = Vector3.Dot(v0, v0);
        float dot01 = Vector3.Dot(v0, v1);
        float dot02 = Vector3.Dot(v0, v2);
        float dot11 = Vector3.Dot(v1, v1);
        float dot12 = Vector3.Dot(v1, v2);
        
        float invDenom = 1 / (dot00 * dot11 - dot01 * dot01);
        float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
        float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
        
        return (u >= 0) && (v >= 0) && (u + v < 1);
    }
    
    bool InTriangle(Vector2 A,Vector2 B, Vector2 C, Vector2 P)
    {
        Vector2 v0 = C - A;
        Vector2 v1 = B - A;
        Vector2 v2 = P - A;
        
        float dot00 = Vector2.Dot(v0, v0);
        float dot01 = Vector2.Dot(v0, v1);
        float dot02 = Vector2.Dot(v0, v2);
        float dot11 = Vector2.Dot(v1, v1);
        float dot12 = Vector2.Dot(v1, v2);
        
        float invDenom = 1 / (dot00 * dot11 - dot01 * dot01);
        float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
        float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
        
        return (u >= 0) && (v >= 0) && (u + v < 1);
    }

    // IEnumerator ProcessComputerShader()
    // {
    //
    // }

    IEnumerator Process()
    {
        yield return null;

        // foreach (var subMesh in m_SubMeshes)
        var subMesh = m_SubMeshes[0];
        {
            for (int i = 0; i < subMesh.Triangles.Length; i += 3)
            {
                int tri0 = subMesh.Triangles[i];
                int tri1 = subMesh.Triangles[i + 1];
                int tri2 = subMesh.Triangles[i + 2];

                Vector3 vert0 = m_Vertices[tri0];
                Vector3 vert1 = m_Vertices[tri1];
                Vector3 vert2 = m_Vertices[tri2];
                
                Vector2 uv0 = m_UVs[tri0];
                Vector2 uv1 = m_UVs[tri1];
                Vector2 uv2 = m_UVs[tri2];

                Vector2 median = (uv0 + uv1 + uv2) / 3f;

                Vector2 normalizedPixelDilate = new Vector2((float)m_TrianglePixelDilate / m_Resolution.x, (float)m_TrianglePixelDilate / m_Resolution.y);

                Vector2 uvMedianDirection0 = (uv0 - median).normalized;
                Vector2 dilatedUv0 = uv0 + uvMedianDirection0 * normalizedPixelDilate;
                Vector2 uvMedianDirection1 = (uv1 - median).normalized;
                Vector2 dilatedUv1 = uv1 + uvMedianDirection1 * normalizedPixelDilate;
                Vector2 uvMedianDirection2 = (uv2 - median).normalized;
                Vector2 dilatedUv2 = uv2 + uvMedianDirection2 * normalizedPixelDilate;
                
                Vector2 minUv = Vector2.Min(Vector2.Min(uv0, uv1), uv2);
                Vector2 maxUv = Vector2.Max(Vector2.Max(uv0, uv1), uv2);

                Vector2Int uvInt0 = new Vector2Int(Mathf.RoundToInt(uv0.x * m_Resolution.x), Mathf.RoundToInt(uv0.y * m_Resolution.y));
                Vector2Int uvInt1 = new Vector2Int(Mathf.RoundToInt(uv1.x * m_Resolution.x), Mathf.RoundToInt(uv1.y * m_Resolution.y));
                Vector2Int uvInt2 = new Vector2Int(Mathf.RoundToInt(uv2.x * m_Resolution.x), Mathf.RoundToInt(uv2.y * m_Resolution.y));

                Vector2Int minUvInt = Vector2Int.Min(Vector2Int.Min(uvInt0, uvInt1), uvInt2);
                Vector2Int maxUvInt = Vector2Int.Max(Vector2Int.Max(uvInt0, uvInt1), uvInt2);
                
                Color color = new Color(Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f), 1);
                
                for (int x = minUvInt.x - m_TrianglePixelDilate; x < maxUvInt.x + m_TrianglePixelDilate; ++x)
                {
                    for (int y = minUvInt.y - m_TrianglePixelDilate; y < maxUvInt.y + m_TrianglePixelDilate; ++y)
                    {
                        Vector2 testUV = new Vector2((float)x / (float)m_Resolution.x, (float)y / (float)m_Resolution.y);
                        if (InTriangle(dilatedUv0, dilatedUv1, dilatedUv2, testUV))
                        {
                            m_Texture2D.SetPixel(x, y, color);
                        }
                    }
                }
                m_Texture2D.Apply();
                yield return null;
            }

            yield return null;
        }
    }
}
