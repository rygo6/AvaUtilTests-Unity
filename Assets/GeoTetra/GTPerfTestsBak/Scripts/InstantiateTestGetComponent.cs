using UnityEngine;

namespace GeoTetra.GTPerfTests
{
    public class InstantiateTestGetComponent : MonoBehaviour
    {
        Collider m_Collider;
        MeshRenderer m_MeshRenderer;
        MeshFilter m_Filter;
        
        Collider m_Collider0;
        MeshRenderer m_MeshRenderer0;
        MeshFilter m_Filter0;
        
        Collider m_Collider1;
        MeshRenderer m_MeshRenderer1;
        MeshFilter m_Filter1;

        void Awake()
        {
            m_Collider = GetComponent<Collider>();
            m_MeshRenderer = GetComponent<MeshRenderer>();
            m_Filter = GetComponent<MeshFilter>();
            
            m_Collider0 = GetComponent<Collider>();
            m_MeshRenderer0 = GetComponent<MeshRenderer>();
            m_Filter0 = GetComponent<MeshFilter>();
            
            m_Collider1 = GetComponent<Collider>();
            m_MeshRenderer1 = GetComponent<MeshRenderer>();
            m_Filter1 = GetComponent<MeshFilter>();
        }
    }
}