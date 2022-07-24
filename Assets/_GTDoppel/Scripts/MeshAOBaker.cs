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
    
    [SerializeField] 
    Material m_AOBakeMaterial;
    
    [SerializeField] 
    Material m_ContributingBakeMaterial;
    
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
    ComputeBuffer m_BakeMatrixBuffer;
    ComputeBuffer m_ContributingMatrixBuffer;
    uint[] m_Args = new uint[5] { 0, 0, 0, 0, 0 };

    void Start()
    {
        m_ContributingBakeMaterial = Instantiate(m_AOBakeMaterial);
        
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

        int mipCount = 1;
        int cellMipSize = m_GridCellRenderSize;
        while (cellMipSize < multiple2RoundUp)
        {
            cellMipSize *= 2;
            mipCount++;
        }
        Debug.Log(mipCount);
        
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
            filterMode = FilterMode.Point,
        };
        m_AOBakeTexture.Create();

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
            filterMode = FilterMode.Point,
        };
        m_DownsizeTexture.Create();
        
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
        
        m_BakeMatrixBuffer = new ComputeBuffer(m_BakeMesh.vertexCount, 16 * sizeof(float), ComputeBufferType.IndirectArguments, ComputeBufferMode.SubUpdates);
        m_ContributingMatrixBuffer = new ComputeBuffer(m_BakeMesh.vertexCount, 16 * sizeof(float), ComputeBufferType.IndirectArguments, ComputeBufferMode.SubUpdates);
        m_ArgsBuffer = new ComputeBuffer(1, m_Args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        
    }

    void OnDestroy()
    {
        m_Vertices.Dispose();
        m_Normals.Dispose();
        m_ArgsBuffer.Dispose();
        m_BakeMatrixBuffer.Dispose();
        m_ContributingMatrixBuffer.Dispose();
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
        m_CommandBuffer.Clear();
        m_CommandBuffer.SetRenderTarget(m_AOBakeTexture);
        m_CommandBuffer.ClearRenderTarget(true, true, Color.white);
        m_CommandBuffer.SetViewProjectionMatrices(m_LookMatrix, m_OrthoMatrix);
        
        var matrixBuffer  = m_BakeMatrixBuffer.BeginWrite<Matrix4x4>(0, m_VertLength);
        var contributingMatrixBuffer  = m_ContributingMatrixBuffer.BeginWrite<Matrix4x4>(0, m_VertLength);
        const int maxVertCount = 1024;
        for (int i = 0; i < m_VertLength && i < maxVertCount; ++i)
        {
            Matrix4x4 bakingTransformMatrix = TransformMatrix(m_MeshFilter.transform, i);
            Matrix4x4 bakingTransformViewMatrix = MultiplyCameraMatrix(bakingTransformMatrix);
            matrixBuffer[i] = bakingTransformViewMatrix;
            
            Matrix4x4 contributingTransformMatrix = Matrix4x4.TRS( m_ContributingMeshes[0].transform.position, m_ContributingMeshes[0].transform.rotation, m_ContributingMeshes[0].transform.lossyScale);
            contributingTransformMatrix = m_MeshFilter.transform.worldToLocalMatrix * contributingTransformMatrix;
            contributingTransformMatrix = bakingTransformMatrix * contributingTransformMatrix;
            Matrix4x4 contributingTransformViewMatrix = MultiplyCameraMatrix(contributingTransformMatrix);
            contributingMatrixBuffer[i] = contributingTransformViewMatrix;
        }
        m_BakeMatrixBuffer.EndWrite<Matrix4x4>(m_VertLength);
        m_ContributingMatrixBuffer.EndWrite<Matrix4x4>(m_VertLength);

        m_AOBakeMaterial.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_AOBakeMaterial.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_AOBakeMaterial.SetInt("_CellSize", m_GridCellRenderSize);
        m_ContributingBakeMaterial.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_ContributingBakeMaterial.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_ContributingBakeMaterial.SetInt("_CellSize", m_GridCellRenderSize);
        
        m_MeshRenderer.sharedMaterial.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_MeshRenderer.sharedMaterial.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_MeshRenderer.sharedMaterial.SetInt("_CellSize", m_GridCellRenderSize);

        m_Args[0] = (uint) m_BakeMesh.GetSubMesh(0).vertexCount;
        m_Args[1] = (uint) m_BakeMesh.vertexCount;
        m_Args[2] = (uint) m_BakeMesh.GetSubMesh(0).indexStart;
        m_Args[3] = (uint) m_BakeMesh.GetSubMesh(0).baseVertex;
        m_ArgsBuffer.SetData(m_Args);

        m_AOBakeMaterial.SetBuffer("_MatrixBuffer", m_BakeMatrixBuffer);
        m_CommandBuffer.DrawMeshInstancedIndirect(m_BakeMesh, 0, m_AOBakeMaterial, -1, m_ArgsBuffer);
        
        m_ContributingBakeMaterial.SetBuffer("_MatrixBuffer", m_ContributingMatrixBuffer);
        m_CommandBuffer.DrawMeshInstancedIndirect(m_ContributingMeshes[0].sharedMesh, 0, m_ContributingBakeMaterial, -1, m_ArgsBuffer);
        
        Graphics.ExecuteCommandBuffer(m_CommandBuffer);

        m_DownsizeComputeShader.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_DownsizeComputeShader.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_DownsizeComputeShader.SetInt("_CellSize", m_GridCellRenderSize);
        m_DownsizeComputeShader.SetTexture(m_DownSizeKernel, "_InputTexture", m_AOBakeTexture);
        m_DownsizeComputeShader.SetTexture(m_DownSizeKernel, "_OutputTexture", m_DownsizeTexture);
        m_DownsizeComputeShader.Dispatch(m_DownSizeKernel,m_ThreadCount.x, m_ThreadCount.y,1);
    }
    
    static Quaternion QuaternionLookRotation(Vector3 forward, Vector3 up)
    {
        Vector3 vector = Vector3.Normalize(forward);
        Vector3 vector2 = Vector3.Normalize(Vector3.Cross(up, vector));
        Vector3 vector3 = Vector3.Cross(vector, vector2);
        var m00 = vector2.x;
        var m01 = vector2.y;
        var m02 = vector2.z;
        var m10 = vector3.x;
        var m11 = vector3.y;
        var m12 = vector3.z;
        var m20 = vector.x;
        var m21 = vector.y;
        var m22 = vector.z;

        float num8 = (m00 + m11) + m22;
        var quaternion = new Quaternion();
        if (num8 > 0f)
        {
            var num = Mathf.Sqrt(num8 + 1f);
            quaternion.w = num * 0.5f;
            num = 0.5f / num;
            quaternion.x = (m12 - m21) * num;
            quaternion.y = (m20 - m02) * num;
            quaternion.z = (m01 - m10) * num;
            return quaternion;
        }
        if ((m00 >= m11) && (m00 >= m22))
        {
            var num7 = Mathf.Sqrt(((1f + m00) - m11) - m22);
            var num4 = 0.5f / num7;
            quaternion.x = 0.5f * num7;
            quaternion.y = (m01 + m10) * num4;
            quaternion.z = (m02 + m20) * num4;
            quaternion.w = (m12 - m21) * num4;
            return quaternion;
        }
        if (m11 > m22)
        {
            var num6 = Mathf.Sqrt(((1f + m11) - m00) - m22);
            var num3 = 0.5f / num6;
            quaternion.x = (m10+ m01) * num3;
            quaternion.y = 0.5f * num6;
            quaternion.z = (m21 + m12) * num3;
            quaternion.w = (m20 - m02) * num3;
            return quaternion; 
        }
        var num5 = Mathf.Sqrt(((1f + m22) - m00) - m11);
        var num2 = 0.5f / num5;
        quaternion.x = (m20 + m02) * num2;
        quaternion.y = (m21 + m12) * num2;
        quaternion.z = 0.5f * num5;
        quaternion.w = (m01 - m10) * num2;
        return quaternion;
    }
    
    public static float ConvertDegToRad(float degrees)
    {
        return (Mathf.PI / 180) * degrees;
    }

    public static Matrix4x4 GetTranslationMatrix(Vector3 position)
    {
        return new Matrix4x4(new Vector4(1, 0, 0, 0),
                             new Vector4(0, 1, 0, 0),
                             new Vector4(0, 0, 1, 0),
                             new Vector4(position.x, position.y, position.z, 1));
    }

    public static Matrix4x4 GetRotationMatrix(Vector3 anglesDeg)
    {
        anglesDeg = new Vector3(ConvertDegToRad(anglesDeg[0]), ConvertDegToRad(anglesDeg[1]), ConvertDegToRad(anglesDeg[2]));

        Matrix4x4 rotationX = new Matrix4x4(new Vector4(1, 0, 0, 0), 
                                            new Vector4(0, Mathf.Cos(anglesDeg[0]), Mathf.Sin(anglesDeg[0]), 0), 
                                            new Vector4(0, -Mathf.Sin(anglesDeg[0]), Mathf.Cos(anglesDeg[0]), 0),
                                            new Vector4(0, 0, 0, 1));

        Matrix4x4 rotationY = new Matrix4x4(new Vector4(Mathf.Cos(anglesDeg[1]), 0, -Mathf.Sin(anglesDeg[1]), 0),
                                            new Vector4(0, 1, 0, 0),
                                            new Vector4(Mathf.Sin(anglesDeg[1]), 0, Mathf.Cos(anglesDeg[1]), 0),
                                            new Vector4(0, 0, 0, 1));

        Matrix4x4 rotationZ = new Matrix4x4(new Vector4(Mathf.Cos(anglesDeg[2]), Mathf.Sin(anglesDeg[2]), 0, 0),
                                            new Vector4(-Mathf.Sin(anglesDeg[2]), Mathf.Cos(anglesDeg[2]), 0, 0),
                                            new Vector4(0, 0, 1, 0),
                                            new Vector4(0, 0, 0, 1));

        return rotationX * rotationY * rotationZ;
    }

    public static Matrix4x4 GetScaleMatrix(Vector3 scale)
    {
        return new Matrix4x4(new Vector4(scale.x, 0, 0, 0),
                             new Vector4(0, scale.y, 0, 0),
                             new Vector4(0, 0, scale.z, 0),
                             new Vector4(0, 0, 0, 1));
    }

    public static Matrix4x4 Get_TRS_Matrix(Vector3 position, Vector3 rotationAngles, Vector3 scale) 
    {
        return GetTranslationMatrix(position) * GetRotationMatrix(rotationAngles) * GetScaleMatrix(scale);
    }
    
    Matrix4x4 TransformMatrix(Transform bakingMeshTransform, int index)
    {
        Vector3 vertNormal = m_Normals[index];
        Vector3 vertPosition = m_Vertices[index];        
        // Vector3 vertNormal = bakingMeshTransform.TransformVector(m_Normals[index]);
        // Vector3 vertPosition = bakingMeshTransform.TransformPoint(m_Vertices[index]);
        // Vector3 vertNormal = bakingMeshTransform.localToWorldMatrix * m_Normals[index];
        // Vector3 vertPosition = bakingMeshTransform.localToWorldMatrix * m_Vertices[index];
        Quaternion vertRot = QuaternionLookRotation(vertNormal, Vector3.up);
        Quaternion rotation = Quaternion.Inverse(vertRot);
        Vector3 position = rotation * -vertPosition;
        Matrix4x4 transformMatrix = Matrix4x4.TRS(position, rotation, Vector3.one);
        // transformMatrix = bakingMeshTransform.localToWorldMatrix * transformMatrix;
        
        // Matrix4x4 transformMatrix = GetTranslationMatrix(position) * Matrix4x4.Rotate(rotation);
        return transformMatrix;
    }

    // Matrix4x4 TransformMatrix(Transform bakingMeshTransform, int index)
    // {
    //     Quaternion vertRot = QuaternionLookRotation(m_Normals[index], Vector3.up);
    
    //     Vector3 originOffset = bakingMeshTransform.position - m_Vertices[index];
    
    //     Vector3 position = (m_BakeCamera.transform.position - m_Vertices[index]) - originOffset;
    //     Quaternion rotation = m_BakeCamera.transform.rotation * Quaternion.Inverse(vertRot);
    //     position += rotation * originOffset;
    
    //     Matrix4x4 transformMatrix = Matrix4x4.TRS(position, rotation, Vector3.one);
    //     // Matrix4x4 transformMatrix = GetTranslationMatrix(position) * Matrix4x4.Rotate(rotation);
    //     return transformMatrix;
    // }
    
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