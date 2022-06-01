using GeoTetra.GTSplines;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Splines;

public class SplineTool : DoppelTool
{
    [SerializeField] GTSplinePool m_SplinePool;
    [SerializeField] DoppelToolbar m_Toolbar;
    [SerializeField] Transform m_Cursor;
    [SerializeField] Transform m_OutAnchorCursor;
    [SerializeField] Transform m_InAnchorCursor;
    
    const float k_TangentMultiplier = .1f;
    PointerEventData m_EnterPointerEventData;
    PointerEventData m_PressPointerEventData;
    
    void Update()
    {
        if (m_PressPointerEventData == null && m_EnterPointerEventData != null)
        {
            m_Cursor.transform.position = m_EnterPointerEventData.pointerCurrentRaycast.worldPosition;
        }
    }

    public override void OnPointerClick(PointerEventData data, ToolData toolData)
    { }

    public override void OnPointerEnter(PointerEventData data, ToolData toolData)
    {
        m_EnterPointerEventData = data;
        if (!data.dragging)
            m_Cursor.transform.position = data.pointerCurrentRaycast.worldPosition;
    }

    public override void OnPointerExit(PointerEventData data, ToolData toolData)
    {
        m_EnterPointerEventData = null;
    }

    public override void OnPointerDown(PointerEventData data, ToolData toolData)
    {
        if (m_Toolbar.CurrentlySelectedItem == null)
        {
            var spline = m_SplinePool.CreateSpline(toolData.InteractionPosition, toolData.InteractionRotation);
            m_Toolbar.CurrentlySelectedItem = spline.gameObject;
        }
        else
        {
            var spline = m_Toolbar.CurrentlySelectedItem.GetComponent<GTSplineContainer>();
            spline.AddKnot(new BezierKnot(toolData.InteractionPosition, 0, 0, toolData.InteractionRotation));
        }

        m_Cursor.position = toolData.InteractionPosition;
        m_Cursor.rotation = toolData.InteractionRotation;
        
        m_PressPointerEventData = data;
    }

    public override void OnPointerUp(PointerEventData data, ToolData toolData)
    {
        m_PressPointerEventData = null;
    }

    public override void OnBeginDrag(PointerEventData data, ToolData toolData)
    {
        m_OutAnchorCursor.gameObject.SetActive(true);
        m_InAnchorCursor.gameObject.SetActive(true);
    }

    public override void OnDrag(PointerEventData data, ToolData toolData)
    {
        m_OutAnchorCursor.position = toolData.InteractionPosition;
        m_InAnchorCursor.localPosition = -m_OutAnchorCursor.localPosition;

        var spline = m_Toolbar.CurrentlySelectedItem.GetComponent<GTSplineContainer>();
            
        int knotCount = spline.NativeSpline.Count;
        if (knotCount == 0)
            return;
            
        var knot = spline.NativeSpline.Knots[knotCount - 1];
        knot.TangentOut = m_OutAnchorCursor.localPosition * k_TangentMultiplier;
        knot.TangentIn = m_InAnchorCursor.localPosition * k_TangentMultiplier;
        spline.UpdateKnot(knotCount - 1, knot);
    }

    public override void OnEndDrag(PointerEventData data, ToolData toolData)
    {
        m_OutAnchorCursor.gameObject.SetActive(false);
        m_InAnchorCursor.gameObject.SetActive(false);
    }
}

public struct ToolData
{
    public Ray SourceRay;
    public Vector3 InteractionPosition;
    public Quaternion InteractionRotation;
}

public abstract class DoppelTool : MonoBehaviour
{
    public abstract void OnPointerClick(PointerEventData data, ToolData toolData);
    public abstract void OnPointerEnter(PointerEventData data, ToolData toolData);
    public abstract void OnPointerExit(PointerEventData data, ToolData toolData);
    public abstract void OnPointerDown(PointerEventData data, ToolData toolData);
    public abstract void OnPointerUp(PointerEventData data, ToolData toolData);
    public abstract void OnBeginDrag(PointerEventData data, ToolData toolData);
    public abstract void OnDrag(PointerEventData data, ToolData toolData);
    public abstract void OnEndDrag(PointerEventData data, ToolData toolData);
}