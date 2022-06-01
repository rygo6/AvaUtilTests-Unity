using GeoTetra.GTSplines;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Splines;

namespace GeoTetra.GTDoppel
{
    public class DoppelMesh : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, ISelectHandler,
        IDeselectHandler
    {
        [SerializeField] Transform m_Cursor;
        [SerializeField] Transform m_OutAnchorCursor;
        [SerializeField] Transform m_InAnchorCursor;

        [SerializeField] GTSplinePool m_GtSplinePool;

        [SerializeField] DoppelToolbar m_Toolbar;

        const float k_MeshOffset = .01f;
        const float k_TangentMultiplier = .1f;
        PointerEventData m_EnterPointerEventData;
        PointerEventData m_PressPointerEventData;
        Ray m_PointerDownRay;
        Vector3 m_PointerDownPosition;
        Quaternion m_PointerDownRotation;

        void Update()
        {
            if (m_PressPointerEventData == null && m_EnterPointerEventData != null)
            {
                m_Cursor.transform.position = m_EnterPointerEventData.pointerCurrentRaycast.worldPosition;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            // Debug.Log($"OnPointerEnter {eventData.GetHashCode()}");
            m_EnterPointerEventData = eventData;
            if (!eventData.dragging)
                m_Cursor.transform.position = eventData.pointerCurrentRaycast.worldPosition;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // Debug.Log($"OnPointerExit {eventData.GetHashCode()}");
            m_EnterPointerEventData = null;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // var clickRay = eventData.pressEventCamera.ScreenPointToRay(eventData.pointerPressRaycast.screenPosition);
            // Vector3 position = clickRay.GetPoint(eventData.pointerPressRaycast.distance - 0.01f);
            // // var position = eventData.pointerCurrentRaycast.worldPosition;
            // var rotation = Quaternion.LookRotation(Vector3.up, eventData.pointerCurrentRaycast.worldNormal);
            // if (m_Toolbar.CurrentlySelectedItem == null)
            // {
            //     var spline = m_SplinePool.CreateSpline(position, rotation);
            //     m_Toolbar.CurrentlySelectedItem = spline;
            // }
            // else
            // {
            //     m_Toolbar.CurrentlySelectedItem.Spline.Add(new BezierKnot(position, 0, 0, rotation));
            // }
        }

        public void OnSelect(BaseEventData eventData)
        {
        }

        public void OnDeselect(BaseEventData eventData)
        {
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            Debug.Log($"OnBeginDrag {eventData.GetHashCode()}");
            m_OutAnchorCursor.gameObject.SetActive(true);
            m_InAnchorCursor.gameObject.SetActive(true);
        }

        public void OnDrag(PointerEventData eventData)
        {
            // Debug.Log($"PointerEventData {eventData.GetHashCode()}");
            (Ray ray, Vector3 position, Quaternion rotation) = MeshClickData(eventData);
            m_OutAnchorCursor.position = position;
            m_InAnchorCursor.localPosition = -m_OutAnchorCursor.localPosition;
            
            int knotCount = m_Toolbar.CurrentlySelectedItem.NativeSpline.Count;
            if (knotCount == 0)
                return;
            
            var knot = m_Toolbar.CurrentlySelectedItem.NativeSpline.Knots[knotCount - 1];
            knot.TangentOut = m_OutAnchorCursor.localPosition * k_TangentMultiplier;
            knot.TangentIn = m_InAnchorCursor.localPosition * k_TangentMultiplier;
            m_Toolbar.CurrentlySelectedItem.UpdateKnot(knotCount - 1, knot);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            // Debug.Log($"OnEndDrag {eventData.GetHashCode()}");
            m_OutAnchorCursor.gameObject.SetActive(false);
            m_InAnchorCursor.gameObject.SetActive(false);
            // if (m_Toolbar.CurrentlySelectedItem != null)
            // {
            //     m_Toolbar.CurrentlySelectedItem.UpdateMeshCollider();
            // }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // Debug.Log($"OnPointerDown {eventData.GetHashCode()}");
            (m_PointerDownRay, m_PointerDownPosition, m_PointerDownRotation) = MeshClickData(eventData);

            if (m_Toolbar.CurrentlySelectedItem == null)
            {
                var spline = m_GtSplinePool.CreateSpline(m_PointerDownPosition, m_PointerDownRotation);
                m_Toolbar.CurrentlySelectedItem = spline;
            }
            else
            {
                m_Toolbar.CurrentlySelectedItem.AddKnot(new BezierKnot(m_PointerDownPosition, 0, 0, m_PointerDownRotation));
            }

            m_Cursor.position = m_PointerDownPosition;
            m_Cursor.rotation = m_PointerDownRotation;

            m_PressPointerEventData = eventData;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            m_PressPointerEventData = null;
        }

        (Ray, Vector3, Quaternion) MeshClickData(PointerEventData eventData)
        {
            var ray = eventData.enterEventCamera.ScreenPointToRay(eventData.pointerCurrentRaycast.screenPosition);
            var position = ray.GetPoint(eventData.pointerCurrentRaycast.distance - k_MeshOffset);
            var rotation = Quaternion.LookRotation(Vector3.up, eventData.pointerCurrentRaycast.worldNormal);
            return (ray, position, rotation);
        }
    }
}