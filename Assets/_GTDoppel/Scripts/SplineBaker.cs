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
    Vector2Int m_Resolution = new(512, 512);

    [SerializeField] 
    MeshRenderer m_MeshRenderer;
    
    [SerializeField] 
    MeshFilter m_MeshFilter;
    
    [SerializeField] 
    Material m_SplineBakerMaterial;

    readonly Matrix4x4 m_LookMatrix = Matrix4x4.TRS(new Vector3(0, 0, -1), Quaternion.identity, Vector3.one);
    readonly Matrix4x4 m_OrthoMatrix = Matrix4x4.Ortho(-1, 1, -1, 1, 0.01f, 2);
    Matrix4x4 m_TransformTRS;
    Texture2D m_Texture2D;
    RenderTexture m_RenderTexture;
    CommandBuffer m_CommandBuffer;
    ComputeBuffer m_Curves;
    ComputeBuffer m_Lengths;
    int m_PriorKnotCount = -1;

    void Start()
    {
        m_RenderTexture = new RenderTexture(m_Resolution.x, m_Resolution.y, 24);
        m_MeshRenderer.material.mainTexture = m_RenderTexture;
        m_CommandBuffer = new CommandBuffer();
        m_CommandBuffer.name = "SplineBaker";
    }


    void Update()
    {
        if (m_SplinePool.Splines.Count == 0)
            return;

        var spline = m_SplinePool.Splines[0];

        int knotCount = spline.NativeSpline.Count;
        if (m_PriorKnotCount != knotCount)
        {
            m_PriorKnotCount = knotCount;

            m_Curves?.Dispose();
            m_Lengths?.Dispose();

            m_Curves = new ComputeBuffer(knotCount, sizeof(float) * 3 * 4);
            m_Lengths = new ComputeBuffer(knotCount, sizeof(float));
        }
        
        var curves = new NativeArray<BezierCurve>(knotCount, Allocator.Temp);
        var lengths = new NativeArray<float>(knotCount, Allocator.Temp);

        var info = new Vector4(knotCount, spline.NativeSpline.Closed ? 1 : 0, spline.NativeSpline.GetLength(), 0);
        for (int i = 0; i < knotCount; ++i)
        {
            curves[i] = spline.NativeSpline.GetCurve(i);
            lengths[i] = spline.NativeSpline.GetCurveLength(i);
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
        m_TransformTRS = Matrix4x4.TRS(m_MeshFilter.transform.position, m_MeshFilter.transform.rotation, m_MeshFilter.transform.lossyScale);
        // Debug.Log($"{m_Renderer.rootBone.lossyScale}");
        m_CommandBuffer.DrawMesh(m_MeshFilter.sharedMesh, m_TransformTRS, m_SplineBakerMaterial, 0);
        Graphics.ExecuteCommandBuffer(m_CommandBuffer);
    }
}
