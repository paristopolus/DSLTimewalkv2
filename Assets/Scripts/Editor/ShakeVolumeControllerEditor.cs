#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CanEditMultipleObjects]
[CustomEditor(typeof(ShakeVolumeController))]
public class ShakeVolumeControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Auto-Assign Components From This GameObject"))
        {
            foreach (Object obj in targets)
            {
                var controller = (ShakeVolumeController)obj;
                Undo.RecordObject(controller, "Auto-Assign ShakeVolumeController Components");
                controller.AutoAssignComponents();
                EditorUtility.SetDirty(controller);
            }
        }
    }
}
#endif
