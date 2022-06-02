using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace GeoTetra.GTDoppel
{
    public class DoppelMesh : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler,
        IPointerDownHandler,
        IPointerUpHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {
        [SerializeField] DoppelToolbar m_Toolbar;

        const float k_MeshOffset = .01f;
        PointerEventData m_EnterPointerEventData;
        PointerEventData m_PressPointerEventData;
        Plane m_LastEnterPlane;
        Coroutine m_UpdateCoroutine;

        IEnumerator UpdateCoroutine()
        {
            while (m_EnterPointerEventData != null || m_PressPointerEventData != null)
            {
                if (m_EnterPointerEventData != null)
                    m_LastEnterPlane = new Plane( m_EnterPointerEventData.enterEventCamera.transform.forward, m_EnterPointerEventData.pointerCurrentRaycast.worldPosition);
                
                var eventData = m_PressPointerEventData ?? m_EnterPointerEventData;
                m_Toolbar.CurrentTool.OnPointerUpdate(eventData as ExtendedPointerEventData,
                    eventData.currentInputModule as InputSystemUIInputModule,
                    ToolData(eventData));
                yield return null;
            }

            m_UpdateCoroutine = null;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            m_EnterPointerEventData = eventData;
            m_Toolbar.CurrentTool.OnPointerEnter(eventData as ExtendedPointerEventData,
                eventData.currentInputModule as InputSystemUIInputModule,
                ToolData(eventData));
            CheckShouldEnable();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            m_Toolbar.CurrentTool.OnPointerExit(eventData as ExtendedPointerEventData,
                eventData.currentInputModule as InputSystemUIInputModule,
                ToolData(eventData));
            m_EnterPointerEventData = null;
            CheckShouldEnable();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            m_Toolbar.CurrentTool.OnPointerClick(eventData as ExtendedPointerEventData,
                eventData.currentInputModule as InputSystemUIInputModule,
                ToolData(eventData));
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            m_Toolbar.CurrentTool.OnBeginDrag(eventData as ExtendedPointerEventData,
                eventData.currentInputModule as InputSystemUIInputModule,
                ToolData(eventData));
        }

        public void OnDrag(PointerEventData eventData)
        {
            
            
            m_Toolbar.CurrentTool.OnDrag(eventData as ExtendedPointerEventData,
                eventData.currentInputModule as InputSystemUIInputModule,
                ToolData(eventData));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            m_Toolbar.CurrentTool.OnEndDrag(eventData as ExtendedPointerEventData,
                eventData.currentInputModule as InputSystemUIInputModule,
                ToolData(eventData));
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            m_PressPointerEventData = eventData;
            m_Toolbar.CurrentTool.OnPointerDown(eventData as ExtendedPointerEventData,
                eventData.currentInputModule as InputSystemUIInputModule,
                ToolData(eventData));
            CheckShouldEnable();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            m_Toolbar.CurrentTool.OnPointerUp(eventData as ExtendedPointerEventData,
                eventData.currentInputModule as InputSystemUIInputModule,
                ToolData(eventData));
            m_PressPointerEventData = null;
            CheckShouldEnable();
        }

        void CheckShouldEnable()
        {
            if (m_EnterPointerEventData != null || m_PressPointerEventData != null)
            {
                m_UpdateCoroutine ??= StartCoroutine(UpdateCoroutine());
            }
        }

        ToolData ToolData(PointerEventData eventData)
        {
            if (m_EnterPointerEventData == null)
            {
                var ray = eventData.enterEventCamera.ScreenPointToRay(eventData.pointerCurrentRaycast.screenPosition);
                m_LastEnterPlane.Raycast(ray, out float distance);
                var position = ray.GetPoint(distance);
                var rotation = Quaternion.LookRotation(Vector3.up, m_LastEnterPlane.normal);
                return new ToolData
                {
                    SourceRay = ray,
                    InteractionPosition = position,
                    InteractionRotation = rotation,
                };
            }
            else
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
}