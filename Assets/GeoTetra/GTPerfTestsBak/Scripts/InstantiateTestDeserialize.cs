using UnityEngine;

namespace GeoTetra.GTPerfTests
{
    public class InstantiateTestDeserialize : MonoBehaviour
    {
        [SerializeField] Collider m_Collider;

        [SerializeField] MeshRenderer m_MeshRenderer;

        [SerializeField] MeshFilter m_Filter;
        
        [SerializeField] Collider m_Collider0;

        [SerializeField] MeshRenderer m_MeshRenderer0;

        [SerializeField] MeshFilter m_Filter0;

        [SerializeField] Collider m_Collider1;

        [SerializeField] MeshRenderer m_MeshRenderer1;

        [SerializeField] MeshFilter m_Filter1;

        
        void OnValidate()
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