using System.Collections.Generic;
using GeoTetra.GTEventSystem;
using UnityEngine;

namespace GeoTetra.GTCamera
{
    public class EmptySpace : EventReceiverBase
    {
        [SerializeField] CameraOrbit m_CameraOrbit;

        protected override float ToolDataPositionOffset => .01f;
        
        protected override List<EventReceiverRelay> GetCurrentTools
        {
            get
            {
                m_Tools.Clear();
                m_Tools.Add(m_CameraOrbit);
                return m_Tools;
            }
        }
        
        readonly List<EventReceiverRelay> m_Tools = new();
    }
}