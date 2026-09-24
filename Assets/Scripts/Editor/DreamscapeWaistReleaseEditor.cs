#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CanEditMultipleObjects]
[CustomEditor(typeof(DreamscapeWaistRelease))]
public class DreamscapeWaistReleaseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Auto-Assign Components From This GameObject"))
        {
            foreach (Object obj in targets)
            {
                var waistRelease = (DreamscapeWaistRelease)obj;
                Undo.RecordObject(waistRelease, "Auto-Assign DreamscapeWaistRelease Components");
                waistRelease.AutoAssignComponents();
                EditorUtility.SetDirty(waistRelease);
            }
        }
    }
}
#endif
