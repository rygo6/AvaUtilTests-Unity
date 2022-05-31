using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class EmptySpace : MonoBehaviour, IPointerClickHandler
{
    [SerializeField]
    DoppelToolbar m_DoppelToolbar;
    
    public void OnPointerClick(PointerEventData eventData)
    {
        m_DoppelToolbar.CurrentlySelectedItem = null;
    }
}
