using UnityEditor;
using UnityEngine;

namespace SVO
{
    [CustomEditor(typeof(MeshToOctree), true)]
    public class MeshToOctreeEditor: Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (GUILayout.Button("Generate"))
            {
                ((MeshToOctree)target).Generate();
            }
        }
    }
}