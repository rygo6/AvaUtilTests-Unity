using System;
using GeoTetra.GTSplines;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

public class SplineBaker : MonoBehaviour
{
    [SerializeField] 
    GTSplinePool m_SplinePool;

    [SerializeField] 
    GTSplineContainer m_TestSpline;

    [SerializeField] 
    SplineContainer m_SplineContainer;
    
    [SerializeField] 
    MeshRenderer m_TestDisplay;

    [SerializeField] 
    Vector2Int m_Resolution = new Vector2Int(512, 512);
    
    [SerializeField] 
    SkinnedMeshRenderer m_Renderer;
    
    [SerializeField] 
    Material m_SplineBakerMaterial;

    [SerializeField] 
    float m_MeshScale = 100;
    
    readonly Matrix4x4 m_LookMatrix = Matrix4x4.TRS(new Vector3(0, 0, -1), Quaternion.identity, Vector3.one);
    readonly Matrix4x4 m_OrthoMatrix = Matrix4x4.Ortho(-1, 1, -1, 1, 0.01f, 2);
    Matrix4x4 m_TransformTRS;
    Texture2D m_Texture2D;
    RenderTexture m_RenderTexture;
    CommandBuffer m_CommandBuffer;
    ComputeBuffer m_Curves;
    ComputeBuffer m_Lengths;

    void Start()
    {
        m_RenderTexture = new RenderTexture(m_Resolution.x, m_Resolution.y, 24);
        m_TestDisplay.material.mainTexture = m_RenderTexture;
        m_Renderer.sharedMaterial.mainTexture = m_RenderTexture;
        
        m_CommandBuffer = new CommandBuffer();
        m_CommandBuffer.name = "SplineBaker";
        m_TransformTRS = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * m_MeshScale);
        
        // m_Curves = new ComputeBuffer(m_TestSpline.NativeSpline.Count, sizeof(float) * 3 * 4);
        // m_Lengths = new ComputeBuffer(m_TestSpline.NativeSpline.Count, sizeof(float));
        // m_Curves.SetData((NativeArray<BezierCurve>)m_TestSpline.Curves);
        // m_Lengths.SetData((NativeArray<float>)m_TestSpline.Lengths);
        // m_SplineBakerMaterial.SetBuffer("curves", m_Curves);
        // m_SplineBakerMaterial.SetBuffer("curveLengths", m_Lengths);
        // m_SplineBakerMaterial.SetVector("info", m_TestSpline.Info);
        Debug.Log($"SplineInfo {m_TestSpline.Info.x} - {m_TestSpline.Info.z}");
    }

    int m_PriorKnotCount = -1;
    void Update()
    {
        int knotCount = m_SplineContainer.Spline.Count;
        if (m_PriorKnotCount != knotCount)
        {
            m_PriorKnotCount = knotCount;

            m_Curves?.Dispose();
            m_Lengths?.Dispose();

            m_Curves = new ComputeBuffer(knotCount, sizeof(float) * 3 * 4);
            m_Lengths = new ComputeBuffer(knotCount, sizeof(float));
        }
        
        var curves = new NativeArray<BezierCurve>(m_SplineContainer.Spline.Count, Allocator.Temp);
        var lengths = new NativeArray<float>(m_SplineContainer.Spline.Count, Allocator.Temp);

        var info = new Vector4(m_SplineContainer.Spline.Count, m_SplineContainer.Spline.Closed ? 1 : 0, m_SplineContainer.Spline.GetLength(), 0);
        for (int i = 0; i < knotCount; ++i)
        {
            curves[i] = m_SplineContainer.Spline.GetCurve(i);
            lengths[i] = m_SplineContainer.Spline.GetCurveLength(i);
        }
        
        m_Curves.SetData(curves);
        m_Lengths.SetData(lengths);
        m_SplineBakerMaterial.SetBuffer("curves", m_Curves);
        m_SplineBakerMaterial.SetBuffer("curveLengths", m_Lengths);
        m_SplineBakerMaterial.SetVector("info", info);
        
        m_CommandBuffer.Clear();
        m_CommandBuffer.SetRenderTarget(m_RenderTexture);
        m_CommandBuffer.ClearRenderTarget(true, true, Color.clear);
        m_CommandBuffer.SetViewProjectionMatrices(m_LookMatrix, m_OrthoMatrix);
        m_CommandBuffer.DrawMesh(m_Renderer.sharedMesh, m_TransformTRS, m_SplineBakerMaterial, 0);
        Graphics.ExecuteCommandBuffer(m_CommandBuffer);
    }
}
