#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// inspector button for wiring DreamscapeGrabbable references on the same GameObject
[CustomEditor(typeof(DreamscapeGrabbable))]
public class DreamscapeGrabbableEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var grabbable = (DreamscapeGrabbable)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Auto-Assign Components From This GameObject"))
        {
            Undo.RecordObject(grabbable, "Auto-Assign DreamscapeGrabbable Components");
            // fills stateSync and left/right attach point children if found
            grabbable.AutoAssignComponents();
            EditorUtility.SetDirty(grabbable);
        }
    }
}
#endif
