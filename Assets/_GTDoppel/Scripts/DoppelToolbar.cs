using UnityEngine;

public class DoppelToolbar : MonoBehaviour
{
    [SerializeField] 
    DoppelTool m_InitialTool;
    
    GameObject m_CurrentlySelectedItem;
    DoppelTool m_CurrentTool;

    public GameObject CurrentlySelectedItem
    {
        get => m_CurrentlySelectedItem;
        set => m_CurrentlySelectedItem = value;
    }

    public DoppelTool CurrentTool
    {
        get => m_CurrentTool;
        set => m_CurrentTool = value;
    }

    void Awake()
    {
        m_CurrentTool = m_InitialTool;
    }
}