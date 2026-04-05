using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(LLuxGPUBoneOverseer))]
public class LLuxGPUBoneOverseerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        LLuxGPUBoneOverseer overseer = (LLuxGPUBoneOverseer)target;

        EditorGUILayout.Space();
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("Auto Collect All Data", GUILayout.Height(30)))
        {
            overseer.AutoCollectAll();
            EditorUtility.SetDirty(overseer);
            foreach (var bone in overseer.BoneSimulations)
            {
                if (bone != null)
                    EditorUtility.SetDirty(bone);
            }
        }
        GUI.backgroundColor = Color.white;
    }
}
