#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CanEditMultipleObjects]
[CustomEditor(typeof(DreamscapeTableRelease))]
public class DreamscapeTableReleaseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Auto-Assign Components From This GameObject"))
        {
            foreach (Object obj in targets)
            {
                var tableRelease = (DreamscapeTableRelease)obj;
                Undo.RecordObject(tableRelease, "Auto-Assign DreamscapeTableRelease Components");
                tableRelease.AutoAssignComponents();
                EditorUtility.SetDirty(tableRelease);
            }
        }
    }
}
#endif
