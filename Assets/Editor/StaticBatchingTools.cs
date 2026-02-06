using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor-only helpers to enable static batching on selected hierarchies without enabling occlusion or distance culling.
/// </summary>
public static class StaticBatchingTools
{
    [MenuItem("Tools/Procedural/Optimize/Mark Selected Static (Batching Only)")]
    private static void MarkSelectedBatchingStaticOnly()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            EditorUtility.DisplayDialog("Static Batching", "Select one or more roots first (e.g., Run1).", "OK");
            return;
        }

        int touched = 0;
        foreach (GameObject root in selected)
        {
            if (root == null) continue;
            Undo.RegisterFullObjectHierarchyUndo(root, "Mark Static (Batching Only)");
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
            {
                if (t == null) continue;
                GameObject go = t.gameObject;
                if (go == null) continue;

                // Keep it minimal: batching static only. (No occlusion flags to avoid pop-in issues on procedural maps.)
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
                flags |= StaticEditorFlags.BatchingStatic;
                GameObjectUtility.SetStaticEditorFlags(go, flags);
                touched++;
            }
        }

        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[StaticBatchingTools] Marked {touched} object(s) with BatchingStatic. Re-enter Play Mode to see batching effect.");
    }
}

