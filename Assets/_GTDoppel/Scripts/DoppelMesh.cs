using System.Collections.Generic;
using GeoTetra.GTEventSystem;
using UnityEngine;

namespace GeoTetra.GTDoppel
{
    public class DoppelMesh : EventReceiverBase
    {
        [SerializeField] 
        DoppelToolbar m_Toolbar;

        protected override float ToolDataPositionOffset => .01f;

        protected override List<EventReceiverRelay> GetCurrentTools
        {
            get
            {
                m_Tools.Clear();
                m_Tools.Add(m_Toolbar.CurrentTool);
                return m_Tools;
            }
        }

        readonly List<EventReceiverRelay> m_Tools = new();
    }
}