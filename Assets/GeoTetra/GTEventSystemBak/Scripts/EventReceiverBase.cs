using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace GeoTetra.GTEventSystem
{
    public abstract class EventReceiverBase : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler,
        IPointerDownHandler,
        IPointerUpHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {
        PointerEventData m_EnterPointerEventData;
        PointerEventData m_PressPointerEventData;
        Plane m_LastEnterPlane;
        Coroutine m_UpdateCoroutine;

        protected abstract float ToolDataPositionOffset { get; }
        protected abstract List<EventReceiverRelay> GetCurrentTools { get; }

        IEnumerator UpdateCoroutine()
        {
            while (m_EnterPointerEventData != null || m_PressPointerEventData != null)
            {
                if (m_EnterPointerEventData != null && m_EnterPointerEventData.enterEventCamera != null)
                    m_LastEnterPlane = new Plane(m_EnterPointerEventData.enterEventCamera.transform.forward, m_EnterPointerEventData.pointerCurrentRaycast.worldPosition);

                var eventData = m_PressPointerEventData ?? m_EnterPointerEventData;
                for (int i = 0; i < GetCurrentTools.Count; ++i)
                    GetCurrentTools[i].OnPointerUpdate(eventData as ExtendedPointerEventData,
                        eventData.currentInputModule as InputSystemUIInputModule,
                        ToolData(eventData));
                yield return null;
            }

            m_UpdateCoroutine = null;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            m_EnterPointerEventData = eventData;
            for (int i = 0; i < GetCurrentTools.Count; ++i)
                GetCurrentTools[i].OnPointerEnter(eventData as ExtendedPointerEventData,
                    eventData.currentInputModule as InputSystemUIInputModule,
                    ToolData(eventData));
            CheckShouldEnable();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            for (int i = 0; i < GetCurrentTools.Count; ++i)
                GetCurrentTools[i].OnPointerExit(eventData as ExtendedPointerEventData,
                    eventData.currentInputModule as InputSystemUIInputModule,
                    ToolData(eventData));
            m_EnterPointerEventData = null;
            CheckShouldEnable();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            for (int i = 0; i < GetCurrentTools.Count; ++i)
                GetCurrentTools[i].OnPointerClick(eventData as ExtendedPointerEventData,
                    eventData.currentInputModule as InputSystemUIInputModule,
                    ToolData(eventData));
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            for (int i = 0; i < GetCurrentTools.Count; ++i)
                GetCurrentTools[i].OnBeginDrag(eventData as ExtendedPointerEventData,
                    eventData.currentInputModule as InputSystemUIInputModule,
                    ToolData(eventData));
        }

        public void OnDrag(PointerEventData eventData)
        {
            for (int i = 0; i < GetCurrentTools.Count; ++i)
                GetCurrentTools[i].OnDrag(eventData as ExtendedPointerEventData,
                    eventData.currentInputModule as InputSystemUIInputModule,
                    ToolData(eventData));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            for (int i = 0; i < GetCurrentTools.Count; ++i)
                GetCurrentTools[i].OnEndDrag(eventData as ExtendedPointerEventData,
                    eventData.currentInputModule as InputSystemUIInputModule,
                    ToolData(eventData));
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            m_PressPointerEventData = eventData;
            for (int i = 0; i < GetCurrentTools.Count; ++i)
                GetCurrentTools[i].OnPointerDown(eventData as ExtendedPointerEventData,
                    eventData.currentInputModule as InputSystemUIInputModule,
                    ToolData(eventData));
            CheckShouldEnable();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            for (int i = 0; i < GetCurrentTools.Count; ++i)
                GetCurrentTools[i].OnPointerUp(eventData as ExtendedPointerEventData,
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

        InteractionData ToolData(PointerEventData eventData)
        {
            if (eventData.enterEventCamera == null)
            {
                return new InteractionData
                {
                    InteractionSourceRay = default,
                    InteractionPosition = default,
                    InteractionRotation = default,
                };
            }
            
            if (m_EnterPointerEventData == null)
            {
                var ray = eventData.enterEventCamera.ScreenPointToRay(eventData.pointerCurrentRaycast.screenPosition);
                m_LastEnterPlane.Raycast(ray, out float distance);
                var position = ray.GetPoint(distance);
                var rotation = Quaternion.LookRotation(Vector3.up, m_LastEnterPlane.normal);
                return new InteractionData
                {
                    InteractionSourceRay = ray,
                    InteractionPosition = position,
                    InteractionRotation = rotation,
                };
            }
            else
            {
                var ray = eventData.enterEventCamera.ScreenPointToRay(eventData.pointerCurrentRaycast.screenPosition);
                var position = ray.GetPoint(eventData.pointerCurrentRaycast.distance - ToolDataPositionOffset);
                var rotation = Quaternion.LookRotation(Vector3.up, eventData.pointerCurrentRaycast.worldNormal);
                return new InteractionData
                {
                    InteractionSourceRay = ray,
                    InteractionPosition = position,
                    InteractionRotation = rotation,
                };
            }
        }
    }
}