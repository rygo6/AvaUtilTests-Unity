using MeshProcess;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VHACD))]
public class VHACDEditor : Editor
{
    const string k_VHACDName = "VHACDHull";
    
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (GUILayout.Button("Generate Convex Meshes"))
        {            
            var vhacd = target as VHACD;
            foreach (var mf in vhacd.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.name.Contains(k_VHACDName))
                {
                    DestroyImmediate(mf.gameObject);
                }
            }
            

            var meshes =vhacd.GenerateConvexMeshes();
            foreach (var mesh in meshes)
            {
                GameObject go = new GameObject(k_VHACDName);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Standard"));
                go.transform.SetParent(vhacd.transform);
            }
        }
    }
}
