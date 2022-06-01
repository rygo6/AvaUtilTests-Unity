using UnityEngine;
using UnityEngine.EventSystems;

namespace GeoTetra.GTDoppel
{
    public class DoppelMesh : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] DoppelToolbar m_Toolbar;

        const float k_MeshOffset = .01f;

        public void OnPointerEnter(PointerEventData eventData)
        {
            m_Toolbar.CurrentTool.OnPointerEnter(eventData, MeshClickData(eventData));
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            m_Toolbar.CurrentTool.OnPointerExit(eventData, MeshClickData(eventData));
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            m_Toolbar.CurrentTool.OnPointerClick(eventData, MeshClickData(eventData));
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            m_Toolbar.CurrentTool.OnBeginDrag(eventData, MeshClickData(eventData));
        }

        public void OnDrag(PointerEventData eventData)
        {
            m_Toolbar.CurrentTool.OnDrag(eventData, MeshClickData(eventData));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            m_Toolbar.CurrentTool.OnEndDrag(eventData, MeshClickData(eventData));
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            m_Toolbar.CurrentTool.OnPointerDown(eventData, MeshClickData(eventData));
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            m_Toolbar.CurrentTool.OnPointerUp(eventData, MeshClickData(eventData));
        }

        static ToolData MeshClickData(PointerEventData eventData)
        {
            var ray = eventData.enterEventCamera.ScreenPointToRay(eventData.pointerCurrentRaycast.screenPosition);
            var position = ray.GetPoint(eventData.pointerCurrentRaycast.distance - k_MeshOffset);
            var rotation = Quaternion.LookRotation(Vector3.up, eventData.pointerCurrentRaycast.worldNormal);
            return new ToolData
            {
                SourceRay = ray,
                InteractionPosition = position,
                InteractionRotation = rotation,
            };
        }
    }
}