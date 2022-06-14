using GeoTetra.GTEventSystem;
using GeoTetra.GTSplines;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.Splines;

public class SplineTool : EventReceiverRelay
{
    [SerializeField] GTSplinePool m_SplinePool;
    [SerializeField] DoppelToolbar m_Toolbar;
    [SerializeField] Transform m_Cursor;
    [SerializeField] Transform m_OutAnchorCursor;
    [SerializeField] Transform m_InAnchorCursor;
    
    const float k_TangentMultiplier = .1f;

    public override void OnPointerUpdate(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
    {
        if (!module.leftClick.action.IsPressed())
        {
            m_Cursor.transform.position = interactionData.InteractionPosition;
            m_Cursor.rotation = interactionData.InteractionRotation;
        }
    }

    public override void OnPointerClick(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
    { }

    public override void OnPointerEnter(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
    {
        if (!module.leftClick.action.IsPressed())
        {
            m_Cursor.transform.position = interactionData.InteractionPosition;
            m_Cursor.rotation = interactionData.InteractionRotation;
        }
    }

    public override void OnPointerExit(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
    {
    }

    public override void OnPointerDown(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
    {
        if (m_Toolbar.CurrentlySelectedItem == null)
        {
            var spline = m_SplinePool.CreateSpline(interactionData.InteractionPosition, interactionData.InteractionRotation);
            m_Toolbar.CurrentlySelectedItem = spline.gameObject;
        }
        else
        {
            var spline = m_Toolbar.CurrentlySelectedItem.GetComponent<GTSplineContainer>();
            spline.AddKnot(new BezierKnot(interactionData.InteractionPosition, 0, 0, interactionData.InteractionRotation));
        }

        m_Cursor.position = interactionData.InteractionPosition;
        m_Cursor.rotation = interactionData.InteractionRotation;
    }

    public override void OnPointerUp(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
    {
    }

    public override void OnBeginDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
    {
        m_OutAnchorCursor.gameObject.SetActive(true);
        m_InAnchorCursor.gameObject.SetActive(true);
    }

    public override void OnDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
    {
        m_OutAnchorCursor.position = interactionData.InteractionPosition;
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

    public override void OnEndDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
    {
        m_OutAnchorCursor.gameObject.SetActive(false);
        m_InAnchorCursor.gameObject.SetActive(false);
    }
}