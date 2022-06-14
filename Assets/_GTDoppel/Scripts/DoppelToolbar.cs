using GeoTetra.GTEventSystem;
using UnityEngine;

public class DoppelToolbar : MonoBehaviour
{
    [SerializeField] 
    EventReceiverRelay m_InitialTool;
    
    GameObject m_CurrentlySelectedItem;
    EventReceiverRelay m_CurrentTool;

    public GameObject CurrentlySelectedItem
    {
        get => m_CurrentlySelectedItem;
        set => m_CurrentlySelectedItem = value;
    }

    public EventReceiverRelay CurrentTool
    {
        get => m_CurrentTool;
        set => m_CurrentTool = value;
    }

    void Awake()
    {
        m_CurrentTool = m_InitialTool;
    }
}