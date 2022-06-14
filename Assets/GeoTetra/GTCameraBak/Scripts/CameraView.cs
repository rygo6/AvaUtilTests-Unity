
using UnityEngine;

namespace GeoTetra.GTCamera
{
    public class CameraView : MonoBehaviour
    {
        [Header("View to start with")] 
        [SerializeField]
        CameraView _attachToView;

        void Update()
        {
            if (_attachToView != null)
            {
                transform.position = _attachToView.transform.position;
                transform.rotation = _attachToView.transform.rotation;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.DrawSphere(transform.position, .1f);
        }
    }
}