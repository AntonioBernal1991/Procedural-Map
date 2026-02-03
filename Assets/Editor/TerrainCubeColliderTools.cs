using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor utility to remove BoxColliders from TerrainCube instances.
/// Useful when you want terrain tiles to be visual-only (no per-tile collisions).
/// </summary>
public static class TerrainCubeColliderTools
{
    private const string TerrainTag = "Terrain";
    private const string TerrainCubeNamePrefix = "TerrainCube";

    [MenuItem("Tools/Terrain/Remove BoxColliders from TerrainCubes (Selected)")]
    private static void RemoveFromSelected()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            EditorUtility.DisplayDialog("Terrain Cubes", "Select a module / map root (or any parent) first.", "OK");
            return;
        }

        int removed = 0;
        int visited = 0;
        var seen = new HashSet<GameObject>();

        foreach (GameObject root in selected)
        {
            if (root == null) continue;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
            {
                if (t == null) continue;
                GameObject go = t.gameObject;
                if (go == null || !seen.Add(go)) continue;
                visited++;
                removed += RemoveBoxCollidersIfTerrainCube(go);
            }
        }

        if (removed > 0)
        {
            EditorSceneManager.MarkAllScenesDirty();
        }

        EditorUtility.DisplayDialog(
            "Terrain Cubes",
            $"Visited {visited} GameObject(s).\nRemoved {removed} BoxCollider(s).",
            "OK");
    }

    [MenuItem("Tools/Terrain/Remove BoxColliders from TerrainCubes (Open Scene)")]
    private static void RemoveFromOpenScene()
    {
        if (!EditorUtility.DisplayDialog(
                "Terrain Cubes",
                "This will remove BoxColliders from ALL TerrainCube objects in the currently open scene(s).\n\nContinue?",
                "Remove",
                "Cancel"))
        {
            return;
        }

        int removed = 0;
        int checkedColliders = 0;

        // Find all BoxColliders in the open scenes (includes inactive).
        BoxCollider[] colliders = Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (BoxCollider bc in colliders)
        {
            if (bc == null) continue;
            checkedColliders++;
            if (EditorUtility.IsPersistent(bc)) continue; // skip assets

            GameObject go = bc.gameObject;
            if (!IsTerrainCube(go)) continue;

            Undo.DestroyObjectImmediate(bc);
            removed++;
        }

        if (removed > 0)
        {
            EditorSceneManager.MarkAllScenesDirty();
        }

        EditorUtility.DisplayDialog(
            "Terrain Cubes",
            $"Checked {checkedColliders} BoxCollider(s).\nRemoved {removed} BoxCollider(s).",
            "OK");
    }

    private static int RemoveBoxCollidersIfTerrainCube(GameObject go)
    {
        if (!IsTerrainCube(go)) return 0;

        BoxCollider[] boxes = go.GetComponents<BoxCollider>();
        int removed = 0;
        for (int i = 0; i < boxes.Length; i++)
        {
            BoxCollider bc = boxes[i];
            if (bc == null) continue;
            Undo.DestroyObjectImmediate(bc);
            removed++;
        }
        return removed;
    }

    private static bool IsTerrainCube(GameObject go)
    {
        if (go == null) return false;

        // Name-based match (covers TerrainCube and TerrainCube(Clone) and variants).
        bool nameMatch = go.name != null && go.name.StartsWith(TerrainCubeNamePrefix);

        // Tag-based match (your prefab uses tag "Terrain").
        bool tagMatch = false;
        try
        {
            tagMatch = go.CompareTag(TerrainTag);
        }
        catch
        {
            // Tag might not exist in some projects. Ignore.
        }

        if (!nameMatch && !tagMatch) return false;

        // Avoid touching special internal objects (just in case).
        if (go.name == "BasePlane") return false;
        if (go.name.Contains("_Combined")) return false;

        // Heuristic: must have a renderer (terrain tile visuals).
        return go.GetComponent<Renderer>() != null;
    }
}

