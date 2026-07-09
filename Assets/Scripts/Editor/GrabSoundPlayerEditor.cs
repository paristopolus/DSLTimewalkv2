#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CanEditMultipleObjects]
[CustomEditor(typeof(GrabSoundPlayer))]
public class GrabSoundPlayerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Auto-Assign Components From This GameObject"))
        {
            foreach (Object obj in targets)
            {
                var grabSoundPlayer = (GrabSoundPlayer)obj;
                Undo.RecordObject(grabSoundPlayer, "Auto-Assign GrabSoundPlayer Components");
                grabSoundPlayer.AutoAssignComponents();
                EditorUtility.SetDirty(grabSoundPlayer);
            }
        }
    }
}
#endif
