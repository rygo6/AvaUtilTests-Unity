using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

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

    [FormerlySerializedAs("m_MeshMaterial")] 
    [SerializeField] 
    Material m_AOBakeMaterial;
    
    [SerializeField] 
    ComputeShader m_DownsizeComputeShader;

    [SerializeField] 
    List<MeshFilter> m_ContributingMeshes;

    [SerializeField] 
    MeshRenderer m_PreviewQuad;

    readonly Matrix4x4 m_LookMatrix = Matrix4x4.TRS(new Vector3(0, 0, -1), Quaternion.identity, Vector3.one);
    readonly Matrix4x4 m_OrthoMatrix = Matrix4x4.Ortho(-1, 1, -1, 1, 0.01f, 2);
    Matrix4x4 m_TransformTRS;
    Texture2D m_Texture2D;
    int m_VertLength;
    
    RenderTexture m_AOBakeTexture;
    RenderTexture m_DownsizeTexture;

    CommandBuffer m_CommandBuffer;
    Mesh m_BakeMesh;
    NativeArray<Vector3> m_Vertices;
    NativeArray<Vector3> m_Normals;
    Vector2Int m_Resolution;
    Vector2Int m_ThreadCount;

    Transform m_TempTransform;

    int m_DownSizeKernel;
    
    ComputeBuffer m_ArgsBuffer;
    uint[] m_Args = new uint[5] { 0, 0, 0, 0, 0 };

    void Start()
    {
        m_TempTransform = new GameObject("TempTransform").transform;

        m_CommandBuffer = new CommandBuffer();
        m_CommandBuffer.name = "MeshAOBaker";

        m_BakeMesh = m_MeshFilter.sharedMesh;

        m_Vertices = new NativeArray<Vector3>(m_BakeMesh.vertices, Allocator.Persistent);
        m_VertLength = m_BakeMesh.vertexCount;

        m_Normals = new NativeArray<Vector3>(m_BakeMesh.normals, Allocator.Persistent);
        
        

        Debug.Log("m_VertLength " + m_VertLength);
        int vertSqrRoot = Mathf.FloorToInt(Mathf.Sqrt(m_VertLength));
        Debug.Log("vertSqrRoot " + vertSqrRoot);
        int vertCellScaled = vertSqrRoot * m_GridCellRenderSize;
        Debug.Log("vertCellScaled " + vertCellScaled);
        int multiple2RoundUp = UpperPowerOfTwo(vertCellScaled);
        Debug.Log("multiple2RoundUp " + multiple2RoundUp);
        m_Resolution = new Vector2Int(multiple2RoundUp, multiple2RoundUp);
        
        m_DownSizeKernel = m_DownsizeComputeShader.FindKernel("Downsize");
        
        RenderTextureDescriptor aoDescription = new RenderTextureDescriptor(
            m_Resolution.x, 
            m_Resolution.y, 
            RenderTextureFormat.Default, 
            0, 
            0)
        {
            autoGenerateMips = false,
            useMipMap = false,
            msaaSamples = 8,
        };

        m_AOBakeTexture = new RenderTexture(aoDescription)
        {
            filterMode = FilterMode.Point
        };
        
        RenderTextureDescriptor downsizeDescription = new RenderTextureDescriptor(
            m_Resolution.x / m_GridCellRenderSize, 
            m_Resolution.y / m_GridCellRenderSize, 
            RenderTextureFormat.Default, 
            0, 
            0)
        {
            autoGenerateMips = false,
            useMipMap = false,
            msaaSamples = 1,
            enableRandomWrite = true
        };

        m_DownsizeTexture = new RenderTexture(downsizeDescription)
        {
            filterMode = FilterMode.Point
        };
        
        m_MeshRenderer.sharedMaterial.mainTexture = m_DownsizeTexture;
        m_PreviewQuad.sharedMaterial.mainTexture = m_AOBakeTexture;
        
        m_AOBakeMaterial.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_AOBakeMaterial.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_AOBakeMaterial.SetInt("_CellSize", m_GridCellRenderSize);
        
        m_MeshRenderer.sharedMaterial.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_MeshRenderer.sharedMaterial.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_MeshRenderer.sharedMaterial.SetInt("_CellSize", m_GridCellRenderSize);
        
        m_DownsizeComputeShader.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_DownsizeComputeShader.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_DownsizeComputeShader.SetInt("_CellSize", m_GridCellRenderSize);
        
        const int threadCount = 8;
        m_ThreadCount.x = m_Resolution.x / m_GridCellRenderSize / threadCount;
        m_ThreadCount.y = m_Resolution.y / m_GridCellRenderSize / threadCount;
        Debug.Log(m_ThreadCount.x + " " + m_ThreadCount.y);
        
        m_ArgsBuffer = new ComputeBuffer(1, m_Args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        m_Args[0] = (uint) m_BakeMesh.triangles.Length;
        m_Args[1] = (uint) m_BakeMesh.vertexCount;
        m_Args[2] = (uint) 0;
        m_Args[3] = (uint) 0;
        m_ArgsBuffer.SetData(m_Args);
    }

    void OnDestroy()
    {
        m_Vertices.Dispose();
        m_Normals.Dispose();
        m_ArgsBuffer.Dispose();
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
        m_AOBakeMaterial.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_AOBakeMaterial.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_AOBakeMaterial.SetInt("_CellSize", m_GridCellRenderSize);
        
        m_MeshRenderer.sharedMaterial.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_MeshRenderer.sharedMaterial.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_MeshRenderer.sharedMaterial.SetInt("_CellSize", m_GridCellRenderSize);
        
        m_CommandBuffer.Clear();
        m_CommandBuffer.SetRenderTarget(m_AOBakeTexture);
        m_CommandBuffer.ClearRenderTarget(true, true, Color.white);
        m_CommandBuffer.SetViewProjectionMatrices(m_LookMatrix, m_OrthoMatrix);
        
        
        List<Matrix4x4> bakingMatrices = new List<Matrix4x4>();
        List<Matrix4x4> contributingMatrices = new List<Matrix4x4>();

        const int maxInstances = 1023;
        for (int i = 0; i < m_VertLength && i < maxInstances; ++i)
        {
            Matrix4x4 bakingTransformMatrix = TransformMatrix(m_MeshFilter.transform, i);
            bakingMatrices.Add(MultiplyCameraMatrix(bakingTransformMatrix));
            
            Matrix4x4 contributingTransformMatrix = Matrix4x4.TRS(m_ContributingMeshes[0].transform.position, m_ContributingMeshes[0].transform.rotation, m_ContributingMeshes[0].transform.lossyScale);
            contributingTransformMatrix = bakingTransformMatrix * contributingTransformMatrix;
            contributingMatrices.Add(MultiplyCameraMatrix(contributingTransformMatrix));
        }
        
        m_CommandBuffer.DrawMeshInstanced(m_MeshFilter.sharedMesh, 0, m_AOBakeMaterial, -1, bakingMatrices.ToArray());
        m_CommandBuffer.DrawMeshInstanced(m_ContributingMeshes[0].sharedMesh, 0, m_AOBakeMaterial, -1, contributingMatrices.ToArray());
        
        
        Graphics.ExecuteCommandBuffer(m_CommandBuffer);
        
        m_DownsizeComputeShader.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_DownsizeComputeShader.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_DownsizeComputeShader.SetInt("_CellSize", m_GridCellRenderSize);
        m_DownsizeComputeShader.SetTexture(m_DownSizeKernel, "_InputTexture", m_AOBakeTexture);
        m_DownsizeComputeShader.SetTexture(m_DownSizeKernel, "_OutputTexture", m_DownsizeTexture);
        m_DownsizeComputeShader.Dispatch(m_DownSizeKernel,m_ThreadCount.x, m_ThreadCount.y,1 );
        
    }

    Matrix4x4 TransformMatrix(Transform bakingMeshTransform, int index)
    {
        m_TempTransform.forward = m_Normals[index];
        Quaternion vertRot = m_TempTransform.rotation;
        Vector3 originOffset = bakingMeshTransform.position - m_Vertices[index];
        m_TempTransform.transform.position = (m_BakeCamera.transform.position - m_Vertices[index]) - originOffset;
        m_TempTransform.transform.rotation = m_BakeCamera.transform.rotation * Quaternion.Inverse(vertRot);
        m_TempTransform.transform.Translate(originOffset, Space.Self);
        m_TempTransform.transform.localScale = bakingMeshTransform.transform.localScale;
        Matrix4x4 transformMatrix = Matrix4x4.TRS(m_TempTransform.position, m_TempTransform.rotation, bakingMeshTransform.lossyScale);
        return transformMatrix;
    }
    
    Matrix4x4 MultiplyCameraMatrix(Matrix4x4 transformMatrix)
    {
        Matrix4x4 compoundMatrix = m_BakeCamera.worldToCameraMatrix * transformMatrix;
        compoundMatrix = m_BakeCamera.projectionMatrix * compoundMatrix;
        return compoundMatrix;
    }
}

public static class MatrixExtensions
{
    public static Quaternion ExtractRotation(this Matrix4x4 matrix)
    {
        Vector3 forward;
        forward.x = matrix.m02;
        forward.y = matrix.m12;
        forward.z = matrix.m22;
 
        Vector3 upwards;
        upwards.x = matrix.m01;
        upwards.y = matrix.m11;
        upwards.z = matrix.m21;
 
        return Quaternion.LookRotation(forward, upwards);
    }
 
    public static Vector3 ExtractPosition(this Matrix4x4 matrix)
    {
        Vector3 position;
        position.x = matrix.m03;
        position.y = matrix.m13;
        position.z = matrix.m23;
        return position;
    }
 
    public static Vector3 ExtractScale(this Matrix4x4 matrix)
    {
        Vector3 scale;
        scale.x = new Vector4(matrix.m00, matrix.m10, matrix.m20, matrix.m30).magnitude;
        scale.y = new Vector4(matrix.m01, matrix.m11, matrix.m21, matrix.m31).magnitude;
        scale.z = new Vector4(matrix.m02, matrix.m12, matrix.m22, matrix.m32).magnitude;
        return scale;
    }
}