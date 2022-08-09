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
    
    [SerializeField] 
    Vector3 m_FinalRotationTest;

    readonly Matrix4x4 m_LookMatrix = Matrix4x4.TRS(new Vector3(0, 0, 0), Quaternion.identity, Vector3.one);
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

    int m_DownSizeKernel;
    
    ComputeBuffer m_BakeArgsBuffer;
    ComputeBuffer m_ContributingArgsBuffer;
    ComputeBuffer m_BakeMatrixBuffer;
    ComputeBuffer m_ContributingMatrixBuffer;
    ComputeBuffer m_VerticesBuffer;
    ComputeBuffer m_NormalsBuffer;
    uint[] m_BakeArgs = new uint[5] { 0, 0, 0, 0, 0 };
    uint[] m_ContributingArgs = new uint[5] { 0, 0, 0, 0, 0 };

    void Start()
    {
        m_ContributingBakeMaterial = Instantiate(m_AOBakeMaterial);

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
        
        const int threadCount = 8;
        m_ThreadCount.x = m_Resolution.x / m_GridCellRenderSize / threadCount;
        m_ThreadCount.y = m_Resolution.y / m_GridCellRenderSize / threadCount;
        m_ThreadCount.x = m_ThreadCount.x < 1 ? 1 : m_ThreadCount.x;
        m_ThreadCount.y = m_ThreadCount.y < 1 ? 1 : m_ThreadCount.y;
        Debug.Log(m_ThreadCount.x + " " + m_ThreadCount.y);
        
        m_BakeMatrixBuffer = new ComputeBuffer(m_BakeMesh.vertexCount, 16 * sizeof(float), ComputeBufferType.IndirectArguments, ComputeBufferMode.SubUpdates);
        m_ContributingMatrixBuffer = new ComputeBuffer(m_BakeMesh.vertexCount, 16 * sizeof(float), ComputeBufferType.IndirectArguments, ComputeBufferMode.SubUpdates);
        m_BakeArgsBuffer = new ComputeBuffer(1, m_BakeArgs.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        m_ContributingArgsBuffer = new ComputeBuffer(1, m_ContributingArgs.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        m_VerticesBuffer = new ComputeBuffer(m_BakeMesh.vertexCount, 3 * sizeof(float), ComputeBufferType.IndirectArguments, ComputeBufferMode.SubUpdates);
        m_NormalsBuffer = new ComputeBuffer(m_BakeMesh.vertexCount, 3 * sizeof(float), ComputeBufferType.IndirectArguments, ComputeBufferMode.SubUpdates);
        
        SetProperties();
    }

    void OnDestroy()
    {
        m_Vertices.Dispose();
        m_Normals.Dispose();
        m_BakeArgsBuffer.Dispose();
        m_ContributingArgsBuffer.Dispose();
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

        m_AOBakeMaterial.SetMatrix("_BakeObject_WorldToLocalMatrix", m_MeshFilter.GetComponent<Renderer>().worldToLocalMatrix);
        m_AOBakeMaterial.SetVector("_BakeObject_LossyScale", m_MeshFilter.transform.lossyScale);
        m_AOBakeMaterial.SetMatrix("_BakeCamera_WorldToCameraMatrix", m_BakeCamera.worldToCameraMatrix);
        m_AOBakeMaterial.SetMatrix("_BakeCamera_ProjectionMatrix", m_BakeCamera.projectionMatrix);
        m_AOBakeMaterial.SetMatrix("_FinalRotationMatrix", Matrix4x4.Rotate(Quaternion.Euler(m_FinalRotationTest)));
        
        m_ContributingBakeMaterial.SetMatrix("_BakeObject_WorldToLocalMatrix", m_MeshFilter.GetComponent<Renderer>().worldToLocalMatrix);
        m_ContributingBakeMaterial.SetMatrix("_ContributingMatrix", m_ContributingMeshes[0].GetComponent<Renderer>().localToWorldMatrix);
        m_ContributingBakeMaterial.SetMatrix("_BakeCamera_WorldToCameraMatrix", m_BakeCamera.worldToCameraMatrix);
        m_ContributingBakeMaterial.SetMatrix("_BakeCamera_ProjectionMatrix", m_BakeCamera.projectionMatrix);
        m_ContributingBakeMaterial.SetMatrix("_FinalRotationMatrix", Matrix4x4.Rotate(Quaternion.Euler(m_FinalRotationTest)));

        // for (int i = 0; i < m_BakeMesh.subMeshCount; ++i) 
        // {
            m_CommandBuffer.DrawMeshInstancedIndirect(m_BakeMesh, 0, m_AOBakeMaterial, -1, m_BakeArgsBuffer);
            m_CommandBuffer.DrawMeshInstancedIndirect(m_ContributingMeshes[0].sharedMesh, 0, m_ContributingBakeMaterial, -1, m_ContributingArgsBuffer);
        // }

        Graphics.ExecuteCommandBuffer(m_CommandBuffer);

        m_DownsizeComputeShader.Dispatch(m_DownSizeKernel, m_ThreadCount.x, m_ThreadCount.y,1);
    }

    void SetProperties()
    {
        m_VerticesBuffer.SetData(m_Vertices);
        m_NormalsBuffer.SetData(m_Normals);
        
        m_AOBakeMaterial.DisableKeyword("CONTRIBUTING_OBJECT");
        m_AOBakeMaterial.SetBuffer("_Vertices", m_VerticesBuffer);
        m_AOBakeMaterial.SetBuffer("_Normals", m_NormalsBuffer);
        m_AOBakeMaterial.SetFloat("_RenderTextureSizeX", m_Resolution.x);
        m_AOBakeMaterial.SetFloat("_RenderTextureSizeY", m_Resolution.y);
        m_AOBakeMaterial.SetFloat("_CellSize", m_GridCellRenderSize);
        m_AOBakeMaterial.SetMatrix("_BakeObject_WorldToLocalMatrix", m_MeshFilter.GetComponent<Renderer>().worldToLocalMatrix);
        m_AOBakeMaterial.SetMatrix("_BakeCamera_WorldToCameraMatrix", m_BakeCamera.worldToCameraMatrix);
        m_AOBakeMaterial.SetMatrix("_BakeCamera_ProjectionMatrix", m_BakeCamera.projectionMatrix);

        m_ContributingBakeMaterial.EnableKeyword("CONTRIBUTING_OBJECT");
        m_ContributingBakeMaterial.SetBuffer("_Vertices", m_VerticesBuffer);
        m_ContributingBakeMaterial.SetBuffer("_Normals", m_NormalsBuffer);
        m_ContributingBakeMaterial.SetFloat("_RenderTextureSizeX", m_Resolution.x);
        m_ContributingBakeMaterial.SetFloat("_RenderTextureSizeY", m_Resolution.y);
        m_ContributingBakeMaterial.SetFloat("_CellSize", m_GridCellRenderSize);
        m_ContributingBakeMaterial.SetMatrix("_BakeObject_WorldToLocalMatrix", m_MeshFilter.GetComponent<Renderer>().worldToLocalMatrix);
        m_ContributingBakeMaterial.SetMatrix("_BakeCamera_WorldToCameraMatrix", m_BakeCamera.worldToCameraMatrix);
        m_ContributingBakeMaterial.SetMatrix("_BakeCamera_ProjectionMatrix", m_BakeCamera.projectionMatrix);

        m_ContributingBakeMaterial.SetMatrix("_ContributingMatrix", m_ContributingMeshes[0].GetComponent<Renderer>().localToWorldMatrix);
        
        m_MeshRenderer.sharedMaterial.SetFloat("_RenderTextureSizeX", m_Resolution.x);
        m_MeshRenderer.sharedMaterial.SetFloat("_RenderTextureSizeY", m_Resolution.y);
        m_MeshRenderer.sharedMaterial.SetFloat("_CellSize", m_GridCellRenderSize);

        int instanceCount = m_BakeMesh.vertexCount;
        
        m_BakeArgs[0] = (uint) m_BakeMesh.GetSubMesh(0).indexCount;
        m_BakeArgs[1] = (uint) instanceCount;
        m_BakeArgs[2] = (uint) m_BakeMesh.GetSubMesh(0).indexStart;
        m_BakeArgs[3] = (uint) m_BakeMesh.GetSubMesh(0).baseVertex;
        m_BakeArgsBuffer.SetData(m_BakeArgs);
        
        m_ContributingArgs[0] = (uint) m_ContributingMeshes[0].sharedMesh.GetSubMesh(0).indexCount;
        m_ContributingArgs[1] = (uint) instanceCount;
        m_ContributingArgs[2] = (uint) m_ContributingMeshes[0].sharedMesh.GetSubMesh(0).indexStart;
        m_ContributingArgs[3] = (uint) m_ContributingMeshes[0].sharedMesh.GetSubMesh(0).baseVertex;
        m_ContributingArgsBuffer.SetData(m_ContributingArgs);
        
        m_DownsizeComputeShader.SetInt("_RenderTextureSizeX", m_Resolution.x);
        m_DownsizeComputeShader.SetInt("_RenderTextureSizeY", m_Resolution.y);
        m_DownsizeComputeShader.SetInt("_CellSize", m_GridCellRenderSize);
        m_DownsizeComputeShader.SetTexture(m_DownSizeKernel, "_InputTexture", m_AOBakeTexture);
        m_DownsizeComputeShader.SetTexture(m_DownSizeKernel, "_OutputTexture", m_DownsizeTexture);
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
    
    Quaternion q_look_at(Vector3 forward, Vector3 up)
    {
        float3 right = Vector3.Cross(forward, up).normalized;
        up = Vector3.Cross(forward, right).normalized;

        float m00 = right.x;
        float m01 = right.y;
        float m02 = right.z;
        float m10 = up.x;
        float m11 = up.y;
        float m12 = up.z;
        float m20 = forward.x;
        float m21 = forward.y;
        float m22 = forward.z;

        float num8 = (m00 + m11) + m22;
        Quaternion q = Quaternion.identity;
        if (num8 > 0.0f)
        {
            float num = Mathf.Sqrt(num8 + 1.0f);
            q.w = num * 0.5f;
            num = 0.5f / num;
            q.x = (m12 - m21) * num;
            q.y = (m20 - m02) * num;
            q.z = (m01 - m10) * num;
            return q;
        }

        if ((m00 >= m11) && (m00 >= m22))
        {
            float num7 = Mathf.Sqrt(((1.0f + m00) - m11) - m22);
            float num4 = 0.5f / num7;
            q.x = 0.5f * num7;
            q.y = (m01 + m10) * num4;
            q.z = (m02 + m20) * num4;
            q.w = (m12 - m21) * num4;
            return q;
        }

        if (m11 > m22)
        {
            float num6 = Mathf.Sqrt(((1.0f + m11) - m00) - m22);
            float num3 = 0.5f / num6;
            q.x = (m10 + m01) * num3;
            q.y = 0.5f * num6;
            q.z = (m21 + m12) * num3;
            q.w = (m20 - m02) * num3;
            return q;
        }

        float num5 = Mathf.Sqrt(((1.0f + m22) - m00) - m11);
        float num2 = 0.5f / num5;
        q.x = (m20 + m02) * num2;
        q.y = (m21 + m12) * num2;
        q.z = 0.5f * num5;
        q.w = (m01 - m10) * num2;
        return q;
    }
    
    public static Matrix4x4 position_to_matrix(Vector3 position)
    {
        return new Matrix4x4(new Vector4(1, 0, 0, 0),
                             new Vector4(0, 1, 0, 0),
                             new Vector4(0, 0, 1, 0),
                             new Vector4(position.x, position.y, position.z, 1));
    }

    Matrix4x4 quaternion_to_matrix(Quaternion quat)
    {
        Matrix4x4 m = Matrix4x4.zero;

        float x = quat.x, y = quat.y, z = quat.z, w = quat.w;
        float x2 = x + x, y2 = y + y, z2 = z + z;
        float xx = x * x2, xy = x * y2, xz = x * z2;
        float yy = y * y2, yz = y * z2, zz = z * z2;
        float wx = w * x2, wy = w * y2, wz = w * z2;
        
        m[0,0] = 1.0f - (yy + zz);
        m[0,1] = xy - wz;
        m[0,2] = xz + wy;

        m[1,0] = xy + wz;
        m[1,1] = 1.0f - (xx + zz);
        m[1,2] = yz - wx;

        m[2,0] = xz - wy;
        m[2,1] = yz + wx;
        m[2,2] = 1.0f - (xx + yy);

        m[3,3] = 1.0f;

        return m;
    }

    public static Matrix4x4 GetScaleMatrix(Vector3 scale)
    {
        return new Matrix4x4(new Vector4(scale.x, 0, 0, 0),
                             new Vector4(0, scale.y, 0, 0),
                             new Vector4(0, 0, scale.z, 0),
                             new Vector4(0, 0, 0, 1));
    }
    
    Vector4 q_conj(Vector4 q)
    {
        return new Vector4(-q.x, -q.y, -q.z, q.w);
    }
    
    Quaternion q_inverse(Vector4 q)
    {
        Vector4 conj = q_conj(q);
        Vector4 div = conj / (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        return new Quaternion(div.x, div.y, div.z, div.w);
    }
    
    public static Vector3 MulQuat(Quaternion rotation, Vector3 point)
    {
        float num1 = rotation.x * 2f;
        float num2 = rotation.y * 2f;
        float num3 = rotation.z * 2f;
        float num4 = rotation.x * num1;
        float num5 = rotation.y * num2;
        float num6 = rotation.z * num3;
        float num7 = rotation.x * num2;
        float num8 = rotation.x * num3;
        float num9 = rotation.y * num3;
        float num10 = rotation.w * num1;
        float num11 = rotation.w * num2;
        float num12 = rotation.w * num3;
        Vector3 vector3;
        vector3.x = (float) ((1.0 - ((double) num5 + (double) num6)) * (double) point.x + ((double) num7 - (double) num12) * (double) point.y + ((double) num8 + (double) num11) * (double) point.z);
        vector3.y = (float) (((double) num7 + (double) num12) * (double) point.x + (1.0 - ((double) num4 + (double) num6)) * (double) point.y + ((double) num9 - (double) num10) * (double) point.z);
        vector3.z = (float) (((double) num8 - (double) num11) * (double) point.x + ((double) num9 + (double) num10) * (double) point.y + (1.0 - ((double) num4 + (double) num5)) * (double) point.z);
        return vector3;
    }

    Matrix4x4 VertexTransformMatrix(int index)
    {
        Vector3 vertNormal = m_Normals[index];
        Vector3 vertPosition = m_Vertices[index];
        Quaternion vertRot = q_look_at(vertNormal, Vector3.up);
        Quaternion rotation = q_inverse(new Vector4(vertRot.x,vertRot.y,vertRot.z,vertRot.w));
        Vector3 position = rotation * -vertPosition;
        Matrix4x4 transformMatrix = position_to_matrix(position) * quaternion_to_matrix(rotation);
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