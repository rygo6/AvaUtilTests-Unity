using UnityEngine;
using UnityEngine.InputSystem.UI;

namespace GeoTetra.GTEventSystem
{
    public struct InteractionData
    {
        public Ray InteractionSourceRay;
        public Vector3 InteractionPosition;
        public Quaternion InteractionRotation;
        public GameObject PressGameObject; // ya it only registers it in EventData on press so save it
    }

    public abstract class EventReceiverRelay : MonoBehaviour
    {
        public abstract void OnPointerUpdate(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData);
        public abstract void OnPointerClick(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData);
        public abstract void OnPointerEnter(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData);
        public abstract void OnPointerExit(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData);
        public abstract void OnPointerDown(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData);
        public abstract void OnPointerUp(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData);
        public abstract void OnBeginDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData);
        public abstract void OnDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData);
        public abstract void OnEndDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData);
    }    
}