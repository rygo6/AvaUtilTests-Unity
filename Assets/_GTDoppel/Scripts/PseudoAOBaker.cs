using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class PseudoAOBaker : MonoBehaviour
{
    [SerializeField] 
    Vector2Int m_Resolution = new(256, 256);

    [SerializeField] 
    MeshRenderer m_MeshRenderer;
    
    [SerializeField] 
    MeshFilter m_MeshFilter;
    
    [SerializeField] 
    Material m_AOBakerMaterial;

    readonly Matrix4x4 m_LookMatrix = Matrix4x4.TRS(new Vector3(0, 0, -1), Quaternion.identity, Vector3.one);
    readonly Matrix4x4 m_OrthoMatrix = Matrix4x4.Ortho(-1, 1, -1, 1, 0.01f, 2);
    Matrix4x4 m_TransformTRS;
    Texture2D m_Texture2D;
    int m_VertLength;
    RenderTexture m_RenderTexture;
    CommandBuffer m_CommandBuffer;
    ComputeBuffer m_VerticesBuffer;
    ComputeBuffer m_NormalsBuffer;
    Mesh.MeshDataArray m_DataArray;
    NativeArray<Vector3> m_Vertices;
    NativeArray<Vector3> m_Normals;

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
        
        m_Normals = new NativeArray<Vector3>(data.vertexCount, Allocator.Persistent);  
        data.GetNormals(m_Normals);

        m_VerticesBuffer = new ComputeBuffer(m_VertLength, sizeof(float) * 3);
        m_NormalsBuffer = new ComputeBuffer(m_VertLength, sizeof(float) * 3);

        m_VerticesBuffer.SetData(m_Vertices);
        m_NormalsBuffer.SetData(m_Normals);
        
        m_AOBakerMaterial.SetBuffer("_vertices", m_VerticesBuffer);
        m_AOBakerMaterial.SetBuffer("_normals", m_NormalsBuffer);
        m_AOBakerMaterial.SetInt("_vertLength", m_VertLength);
    }

    void OnDestroy()
    {
        m_Vertices.Dispose();
        m_Normals.Dispose();
        m_DataArray.Dispose();
    }

    void Update()
    {
        if (Keyboard.current.f8Key.wasReleasedThisFrame)
        {
            Bake();
        }

        Bake();
    }
    
    void Bake()
    {
        m_AOBakerMaterial.SetBuffer("_vertices", m_VerticesBuffer);
        m_AOBakerMaterial.SetBuffer("_normals", m_NormalsBuffer);
        m_AOBakerMaterial.SetInt("_vertLength", m_VertLength);

        m_CommandBuffer.Clear();
        m_CommandBuffer.SetRenderTarget(m_RenderTexture);
        m_CommandBuffer.ClearRenderTarget(true, true, Color.clear);
        m_CommandBuffer.SetViewProjectionMatrices(m_LookMatrix, m_OrthoMatrix);
        m_TransformTRS = Matrix4x4.TRS(m_MeshFilter.transform.position, m_MeshFilter.transform.rotation, m_MeshFilter.transform.lossyScale);
        m_CommandBuffer.DrawMesh(m_MeshFilter.sharedMesh, m_TransformTRS, m_AOBakerMaterial, 0);
        Graphics.ExecuteCommandBuffer(m_CommandBuffer);
    }
}
