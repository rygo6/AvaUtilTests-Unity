using UnityEngine;
using UnityEngine.UIElements;

public class MainUI : MonoBehaviour
{
    [SerializeField]
    UIDocument m_UIDocument;

    void Awake()
    {
        var root = m_UIDocument.rootVisualElement;

        var toolbar = root.Q<VisualElement>("Toolbar");

        var createPointButton = new Button
        {
            text = "Curve\nTool"
        };
        createPointButton.AddToClassList("toolbar-button");
        createPointButton.clicked += CreatePointButtonOnClicked;

        toolbar.Add(createPointButton);
    }

    void CreatePointButtonOnClicked()
    {

    }
}