using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class BruteAOBaker : MonoBehaviour
{
    [SerializeField] 
    MeshFilter m_MeshFilter;

    [SerializeField] 
    ComputeShader m_ComputeShader;

    [SerializeField] 
    float _Mult = .8f;

    [SerializeField] 
    float _Bias = .4f;
    
    [SerializeField] 
    float _AngleMin = .2f;
    
    [SerializeField] 
    float _AngleMax = .8f;

    [SerializeField] 
    int _HeightSteps = 4;
    
    [SerializeField] 
    int _RotationSteps = 4;
    
    [SerializeField] 
    float _SurfaceOffset = .01f;
    
    int m_VertLength;
    int m_IndicesLength;
    ComputeBuffer m_VerticesBuffer;
    ComputeBuffer m_AOVertsBuffer;
    ComputeBuffer m_IndicesBuffer;
    ComputeBuffer m_NormalsBuffer;
    ComputeBuffer m_TangentsBuffer;
    Mesh.MeshDataArray m_DataArray;
    NativeArray<Vector3> m_Vertices;
    NativeArray<Vector3> m_Normals;
    NativeArray<Vector4> m_Tangents;
    NativeArray<int> m_Indices;
    int m_BruteAOVertBakeKernel;
    
    void Start()
    {
        m_DataArray = Mesh.AcquireReadOnlyMeshData(m_MeshFilter.sharedMesh);
        var data = m_DataArray[0];

        m_Vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Persistent);
        data.GetVertices(m_Vertices);
        m_VertLength = data.vertexCount;

        m_Normals = new NativeArray<Vector3>(data.vertexCount, Allocator.Persistent);
        data.GetNormals(m_Normals);

        m_Tangents = new NativeArray<Vector4>(data.vertexCount, Allocator.Persistent);
        data.GetTangents(m_Tangents);

        var subMesh = data.GetSubMesh(0);
        m_Indices = new NativeArray<int>(subMesh.indexCount, Allocator.Persistent);
        data.GetIndices(m_Indices, 0);
        m_IndicesLength = subMesh.indexCount;

        m_VerticesBuffer = new ComputeBuffer(m_VertLength, sizeof(float) * 3);
        m_NormalsBuffer = new ComputeBuffer(m_VertLength, sizeof(float) * 3);
        m_TangentsBuffer = new ComputeBuffer(m_VertLength, sizeof(float) * 4);
        m_AOVertsBuffer = new ComputeBuffer(m_VertLength, sizeof(float));
        m_IndicesBuffer = new ComputeBuffer(m_IndicesLength, sizeof(int));

        m_VerticesBuffer.SetData(m_Vertices);
        m_NormalsBuffer.SetData(m_Normals);
        m_TangentsBuffer.SetData(m_Tangents);
        m_IndicesBuffer.SetData(m_Indices);

        m_BruteAOVertBakeKernel = m_ComputeShader.FindKernel("BruteAOVertBake");
    }

    void OnDestroy()
    {
        m_Vertices.Dispose();
        m_Indices.Dispose();
        m_Normals.Dispose();
        m_Tangents.Dispose();
        m_DataArray.Dispose();
    }

    void Update()
    {
        if (Keyboard.current.f8Key.wasReleasedThisFrame)
        {
            StartCoroutine(Bake());
        }
    }

    IEnumerator Bake()
    {
        m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_vertices", m_VerticesBuffer);
        m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_normals", m_NormalsBuffer);
        m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_tangents", m_TangentsBuffer);
        m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_indices", m_IndicesBuffer);
        m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_aoVertDist", m_AOVertsBuffer);
        m_ComputeShader.SetInt("vertLength", m_VertLength);
        m_ComputeShader.SetInt("indicesLength", m_IndicesLength);
        m_ComputeShader.SetMatrix("unity_ObjectToWorld", m_MeshFilter.transform.localToWorldMatrix);
        m_ComputeShader.SetMatrix("unity_WorldToObject", m_MeshFilter.transform.worldToLocalMatrix);
        m_ComputeShader.SetFloat("_Mult", _Mult);
        m_ComputeShader.SetFloat("_Bias", _Bias);
        m_ComputeShader.SetFloat("_SurfaceOffset", _SurfaceOffset);
        
        Color[] colorArray = new Color[m_VertLength];
        float[] aoArray = new float[m_VertLength];
        float aoSampleCount = 0;

        int count = 0;
        for (int h = 0; h < _HeightSteps; ++h)
        {
            float heightAngle = Mathf.Lerp(_AngleMin, _AngleMax, (1f / _HeightSteps) * h);
            Debug.Log("heightAngle  " + heightAngle);
            m_ComputeShader.SetFloat("_HeightAngle", heightAngle);
            
            for (int r = 0; r < _RotationSteps; ++r)
            {
                count++;
                
                float yAngle = (1f / _RotationSteps) * r;
                Debug.Log("yAngle  " + yAngle);
                m_ComputeShader.SetFloat("_YAngle", yAngle);

                int vertThreadGroups = Mathf.CeilToInt((float) m_VertLength / 64f);
                m_ComputeShader.Dispatch(m_BruteAOVertBakeKernel, vertThreadGroups, 1, 1);
                
                m_AOVertsBuffer.GetData(aoArray);
                
                // float multiplier = .1f;
                for (int i = 0; i < m_VertLength; ++i)
                {
                    colorArray[i].r = ((colorArray[i].r * (count - 1)) + aoArray[i]) / count;
                    colorArray[i].g = ((colorArray[i].g * (count - 1)) + aoArray[i]) / count;
                    colorArray[i].b = ((colorArray[i].b * (count - 1)) + aoArray[i]) / count;
                    colorArray[i].a = 1;
                }
                
                m_MeshFilter.sharedMesh.colors = colorArray;
                m_MeshFilter.sharedMesh.UploadMeshData(false);
                yield return null;
            }
        }
    }
}