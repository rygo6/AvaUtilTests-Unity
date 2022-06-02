using GeoTetra.GTSplines;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.Splines;

public class SplineTool : DoppelTool
{
    [SerializeField] GTSplinePool m_SplinePool;
    [SerializeField] DoppelToolbar m_Toolbar;
    [SerializeField] Transform m_Cursor;
    [SerializeField] Transform m_OutAnchorCursor;
    [SerializeField] Transform m_InAnchorCursor;
    
    const float k_TangentMultiplier = .1f;

    public override void OnPointerUpdate(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData)
    {
        if (!module.leftClick.action.IsPressed())
        {
            m_Cursor.transform.position = toolData.InteractionPosition;
            m_Cursor.rotation = toolData.InteractionRotation;
        }
    }

    public override void OnPointerClick(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData)
    { }

    public override void OnPointerEnter(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData)
    {
        if (!module.leftClick.action.IsPressed())
        {
            m_Cursor.transform.position = toolData.InteractionPosition;
            m_Cursor.rotation = toolData.InteractionRotation;
        }
    }

    public override void OnPointerExit(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData)
    {
    }

    public override void OnPointerDown(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData)
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
    }

    public override void OnPointerUp(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData)
    {
    }

    public override void OnBeginDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData)
    {
        m_OutAnchorCursor.gameObject.SetActive(true);
        m_InAnchorCursor.gameObject.SetActive(true);
    }

    public override void OnDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData)
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

    public override void OnEndDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData)
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
    public abstract void OnPointerUpdate(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData);
    public abstract void OnPointerClick(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData);
    public abstract void OnPointerEnter(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData);
    public abstract void OnPointerExit(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData);
    public abstract void OnPointerDown(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData);
    public abstract void OnPointerUp(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData);
    public abstract void OnBeginDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData);
    public abstract void OnDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData);
    public abstract void OnEndDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, ToolData toolData);
}