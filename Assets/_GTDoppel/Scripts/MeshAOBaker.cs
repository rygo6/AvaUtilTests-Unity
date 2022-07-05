using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class MeshAOBaker : MonoBehaviour
{
    [SerializeField] 
    Camera m_BakeCamera;

    [SerializeField] 
    int m_GridCellRenderSize = 32;

    [SerializeField] 
    MeshRenderer m_MeshRenderer;

    [SerializeField] 
    MeshFilter m_MeshFilter;

    [SerializeField] 
    Material m_MeshMaterial;

    [SerializeField] 
    List<MeshFilter> m_ContributingMeshes;

    [SerializeField] 
    MeshRenderer m_PreviewQuad;

    [SerializeField] 
    int m_TestIndex;

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
    Vector2Int m_Resolution;

    Transform m_TempTransform;

    void Start()
    {
        m_TempTransform = new GameObject("TempTransform").transform;

        m_CommandBuffer = new CommandBuffer();
        m_CommandBuffer.name = "MeshAOBaker";

        m_DataArray = Mesh.AcquireReadOnlyMeshData(m_MeshFilter.sharedMesh);
        var data = m_DataArray[0];

        m_Vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Persistent);
        data.GetVertices(m_Vertices);
        m_VertLength = data.vertexCount;

        m_Normals = new NativeArray<Vector3>(data.vertexCount, Allocator.Persistent);
        data.GetNormals(m_Normals);

        m_VerticesBuffer = new ComputeBuffer(m_VertLength, sizeof(float) * 3);
        m_NormalsBuffer = new ComputeBuffer(m_VertLength, sizeof(float) * 3);

        Debug.Log("m_VertLength " + m_VertLength);
        int vertSqrRoot = Mathf.FloorToInt(Mathf.Sqrt(m_VertLength));
        Debug.Log("vertSqrRoot " + vertSqrRoot);
        int vertCellScaled = vertSqrRoot * m_GridCellRenderSize;
        Debug.Log("vertCellScaled " + vertCellScaled);
        int multiple2RoundUp = UpperPowerOfTwo(vertCellScaled);
        Debug.Log("multiple2RoundUp " + multiple2RoundUp);
        m_Resolution = new Vector2Int(multiple2RoundUp, multiple2RoundUp);

        RenderTextureDescriptor descriptor = new RenderTextureDescriptor(m_Resolution.x, m_Resolution.y, RenderTextureFormat.Default, 0, 0)
        {
            autoGenerateMips = false,
            useMipMap = false,
            msaaSamples = 8,
        };

        m_RenderTexture = new RenderTexture(descriptor)
        {
            filterMode = FilterMode.Point
        };
        
        m_MeshRenderer.sharedMaterial.mainTexture = m_RenderTexture;
        m_PreviewQuad.sharedMaterial.mainTexture = m_RenderTexture;
        m_MeshMaterial.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_MeshMaterial.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_MeshMaterial.SetInt("_CellSize", m_GridCellRenderSize);
        
        m_MeshRenderer.sharedMaterial.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_MeshRenderer.sharedMaterial.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_MeshRenderer.sharedMaterial.SetInt("_CellSize", m_GridCellRenderSize);
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

    int UpperPowerOfTwo(int v)
    {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        v++;
        return v;
    }

    void Bake()
    {
        m_MeshMaterial.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_MeshMaterial.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_MeshMaterial.SetInt("_CellSize", m_GridCellRenderSize);
        
        m_MeshRenderer.sharedMaterial.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_MeshRenderer.sharedMaterial.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_MeshRenderer.sharedMaterial.SetInt("_CellSize", m_GridCellRenderSize);
        
        m_CommandBuffer.Clear();
        m_CommandBuffer.SetRenderTarget(m_RenderTexture);
        m_CommandBuffer.ClearRenderTarget(true, true, Color.white);
        m_CommandBuffer.SetViewProjectionMatrices(m_LookMatrix, m_OrthoMatrix);

        List<Matrix4x4> matrices = new List<Matrix4x4>();
        List<Matrix4x4> contributingMatrices = new List<Matrix4x4>();

        const int maxInstances = 1023;
        for (int i = 0; i < m_VertLength && i < maxInstances; ++i)
        {
            matrices.Add(TransformMatrix(m_MeshFilter.transform, i));
            contributingMatrices.Add(TransformMatrix(m_ContributingMeshes[0].transform, i));
        }

        m_CommandBuffer.DrawMeshInstanced(m_MeshFilter.sharedMesh, 0, m_MeshMaterial, -1, matrices.ToArray());
        m_CommandBuffer.DrawMeshInstanced(m_ContributingMeshes[0].sharedMesh, 0, m_MeshMaterial, -1, contributingMatrices.ToArray());


        Graphics.ExecuteCommandBuffer(m_CommandBuffer);
    }

    Matrix4x4 TransformMatrix(Transform meshTranform, int index)
    {
        m_TempTransform.forward = m_Normals[index];
        Quaternion vertRot = m_TempTransform.rotation;
        Vector3 originOffset = meshTranform.position - m_Vertices[index];
        m_TempTransform.transform.position = (m_BakeCamera.transform.position - m_Vertices[index]) - originOffset;
        m_TempTransform.transform.rotation = m_BakeCamera.transform.rotation * Quaternion.Inverse(vertRot);
        m_TempTransform.transform.Translate(originOffset, Space.Self);
        Matrix4x4 transformMatrix = Matrix4x4.TRS(m_TempTransform.position, m_TempTransform.rotation, meshTranform.localScale);
        Matrix4x4 compoundMatrix = m_BakeCamera.worldToCameraMatrix * transformMatrix;
        compoundMatrix = m_BakeCamera.projectionMatrix * compoundMatrix;
        return compoundMatrix;
    }

    void Bake2()
    {
        List<Matrix4x4> matrices = new List<Matrix4x4>();
        // for (int i = 0; i < 1; i++)
        // {
        int i = m_TestIndex;

        m_TempTransform.forward = m_Normals[i];
        Quaternion vertRot = m_TempTransform.rotation;
        Vector3 originOffset = m_MeshFilter.transform.position - m_Vertices[i];
        m_TempTransform.transform.position = (m_BakeCamera.transform.position - m_Vertices[i]) - originOffset;
        m_TempTransform.transform.rotation = m_BakeCamera.transform.rotation * Quaternion.Inverse(vertRot);
        m_TempTransform.transform.Translate(originOffset, Space.Self);
        Matrix4x4 transformMatrix =
            Matrix4x4.TRS(m_TempTransform.position, m_TempTransform.rotation, m_TempTransform.localScale);
        Matrix4x4 compoundMatrix = m_BakeCamera.worldToCameraMatrix * transformMatrix;
        compoundMatrix = m_BakeCamera.projectionMatrix * compoundMatrix;

        // matrices.Add(camMatrix);
        matrices.Add(compoundMatrix);
        // matrices.Add(m_TempTransform.localToWorldMatrix);
        Debug.DrawRay(m_Vertices[i], m_Normals[i], Color.green);
        // }

        Graphics.DrawMeshInstanced(m_MeshFilter.sharedMesh, 0, m_MeshMaterial, matrices);


        // m_AOBakerMaterial.SetBuffer("_vertices", m_VerticesBuffer);
        // m_AOBakerMaterial.SetBuffer("_normals", m_NormalsBuffer);
        // m_AOBakerMaterial.SetInt("_vertLength", m_VertLength);
        //
        // m_CommandBuffer.Clear();
        // m_CommandBuffer.SetRenderTarget(m_RenderTexture);
        // m_CommandBuffer.ClearRenderTarget(true, true, Color.clear);
        // m_CommandBuffer.SetViewProjectionMatrices(m_LookMatrix, m_OrthoMatrix);
        // m_TransformTRS = Matrix4x4.TRS(m_MeshFilter.transform.position, m_MeshFilter.transform.rotation, m_MeshFilter.transform.lossyScale);
        // m_CommandBuffer.DrawMesh(m_MeshFilter.sharedMesh, m_TransformTRS, m_AOBakerMaterial, 0);
        // Graphics.ExecuteCommandBuffer(m_CommandBuffer);
    }
}