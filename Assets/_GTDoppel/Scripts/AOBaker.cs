using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class AOBaker : MonoBehaviour
{
    [SerializeField] 
    Vector2Int m_Resolution = new(256, 256);

    [SerializeField] 
    MeshRenderer m_MeshRenderer;
    
    [SerializeField] 
    MeshFilter m_MeshFilter;
    
    [SerializeField] 
    Material m_AOBakerMaterial;

    [SerializeField] Transform m_TestTransform;

    readonly Matrix4x4 m_LookMatrix = Matrix4x4.TRS(new Vector3(0, 0, -1), Quaternion.identity, Vector3.one);
    readonly Matrix4x4 m_OrthoMatrix = Matrix4x4.Ortho(-1, 1, -1, 1, 0.01f, 2);
    Matrix4x4 m_TransformTRS;
    Texture2D m_Texture2D;
    int m_VertLength;
    int m_IndicesLength;
    RenderTexture m_RenderTexture;
    CommandBuffer m_CommandBuffer;
    ComputeBuffer m_VerticesBuffer;
    ComputeBuffer m_AOVertsBuffer;
    ComputeBuffer m_IndicesBuffer;
    // ComputeBuffer m_NormalsBuffer;
    Mesh.MeshDataArray m_DataArray;
    NativeArray<Vector3> m_Vertices;
    // NativeArray<Vector3> m_Normals;
    NativeArray<int> m_Indices;

    void Start()
    {
        m_RenderTexture = new RenderTexture(m_Resolution.x, m_Resolution.y, 24);
        m_MeshRenderer.material.mainTexture = m_RenderTexture;
        m_CommandBuffer = new CommandBuffer();
        m_CommandBuffer.name = "AOBaker";
        
        m_TransformTRS = Matrix4x4.TRS(m_MeshFilter.transform.position, m_MeshFilter.transform.rotation, m_MeshFilter.transform.lossyScale);
        
        m_DataArray = Mesh.AcquireReadOnlyMeshData(m_MeshFilter.sharedMesh);
        var data = m_DataArray[0];
        m_Vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Persistent);  
        data.GetVertices(m_Vertices);
        m_VertLength = data.vertexCount;
        
        var subMesh = data.GetSubMesh(0);
        m_Indices = new NativeArray<int>(subMesh.indexCount, Allocator.Persistent);
        data.GetIndices(m_Indices, 0);
        m_IndicesLength = subMesh.indexCount;

        m_VerticesBuffer = new ComputeBuffer(m_VertLength, sizeof(float) * 3);
        m_AOVertsBuffer = new ComputeBuffer(m_VertLength, sizeof(float));
        // m_NormalsBuffer = new ComputeBuffer(m_VertLength, sizeof(float) * 3);
        m_IndicesBuffer = new ComputeBuffer(m_IndicesLength, sizeof(int));
        
        m_VerticesBuffer.SetData(m_Vertices);
        // m_NormalsBuffer.SetData(m_Normals);
        m_IndicesBuffer.SetData(m_Indices);
        
        
        m_AOBakerMaterial.SetBuffer("verts", m_VerticesBuffer);
        // m_AOBakerMaterial.SetBuffer("_normals", m_NormalsBuffer);
        m_AOBakerMaterial.SetBuffer("indices", m_IndicesBuffer);
        m_AOBakerMaterial.SetInt("numVerts", m_VertLength);
        m_AOBakerMaterial.SetInt("numIndices", m_IndicesLength);
        m_AOBakerMaterial.SetBuffer("aoVerts", m_AOVertsBuffer);
    }

    void OnDestroy()
    {
        m_Vertices.Dispose();
        m_Indices.Dispose();
        // m_Normals.Dispose();
        m_DataArray.Dispose();
    }

    void Update()
    {
        if (Keyboard.current.f8Key.wasReleasedThisFrame)
        {
            Bake();
        }
    }
    
    void Bake()
    {
        m_AOBakerMaterial.SetBuffer("verts", m_VerticesBuffer);
        m_AOBakerMaterial.SetBuffer("indices", m_IndicesBuffer);
        m_AOBakerMaterial.SetInt("numVerts", m_VertLength);
        m_AOBakerMaterial.SetInt("numIndices", m_IndicesLength);
        m_AOBakerMaterial.SetBuffer("aoVerts", m_AOVertsBuffer);
        
        m_CommandBuffer.Clear();
        m_CommandBuffer.SetRenderTarget(m_RenderTexture);
        m_CommandBuffer.ClearRenderTarget(true, true, Color.clear);
        m_CommandBuffer.SetViewProjectionMatrices(m_LookMatrix, m_OrthoMatrix);
        m_TransformTRS = Matrix4x4.TRS(m_MeshFilter.transform.position, m_MeshFilter.transform.rotation, m_MeshFilter.transform.lossyScale);
        m_CommandBuffer.DrawMesh(m_MeshFilter.sharedMesh, m_TransformTRS, m_AOBakerMaterial, 0);
        Graphics.ExecuteCommandBuffer(m_CommandBuffer);

        float[] aoArray = new float[m_VertLength];
        m_AOVertsBuffer.GetData(aoArray);
        Color[] colorArray = new Color[m_VertLength];
        for (int i = 0; i < m_VertLength; ++i)
        {
            colorArray[i] = new Color(aoArray[i], aoArray[i], aoArray[i], 1);
        }

        m_MeshFilter.sharedMesh.colors = colorArray;
        m_MeshFilter.sharedMesh.UploadMeshData(false);
    }

    void OnDrawGizmos()
    {
        // for (int v = 0; v < 10; ++v)
        // {
        //     Vector3 pos = m_Vertices[v];
        //     Handles.Label(pos, v.ToString());
        // }
    }
}
