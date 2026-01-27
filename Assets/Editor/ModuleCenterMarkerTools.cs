using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor-only utilities to mark/unmark the central cube of generated modules.
/// Workflow:
/// - Select one or more Module_* GameObjects (module roots) OR a map root that contains them.
/// - Use Tools/Markers menu items to apply/remove ManualCubeMarker (+ TurnCueMarker) on each module's center cube.
/// </summary>
public static class ModuleCenterMarkerTools
{
    private const string DefaultModulePrefix = "Module_";

    private const bool IgnoreBasePlaneDefault = true;
    private const bool IgnoreCombinedDefault = true;

    [MenuItem("Tools/Markers/Mark center cube (selected modules or map root)")]
    private static void MarkSelected()
    {
        ProcessSelection(mark: true);
    }

    [MenuItem("Tools/Markers/Unmark center cube (selected modules or map root)")]
    private static void UnmarkSelected()
    {
        ProcessSelection(mark: false);
    }

    private static void ProcessSelection(bool mark)
    {
        // If you're marking objects generated at runtime, they will NOT persist after stopping Play Mode or reopening.
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(
                "Module Center Markers",
                "You are in Play Mode (or entering/exiting Play Mode).\n\n" +
                "Changes done to procedurally spawned modules won't persist after you stop Play Mode or reopen the project.\n\n" +
                "Tip: generate/bake the map in Edit Mode, then run this tool and SAVE THE SCENE (Ctrl+S).",
                "OK");
            return;
        }

        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            EditorUtility.DisplayDialog("Module Center Markers", "Select one or more Module_* objects or a map root first.", "OK");
            return;
        }

        // Collect modules from selection: if selection includes modules, use them;
        // otherwise treat selection objects as roots and search descendants for Module_*.
        List<Transform> modules = CollectModulesFromSelection(selected, DefaultModulePrefix);
        if (modules.Count == 0)
        {
            EditorUtility.DisplayDialog("Module Center Markers",
                $"No modules found. Expected objects named like '{DefaultModulePrefix}...'.\n" +
                "Tip: select the map root that contains Module_* children.",
                "OK");
            return;
        }

        int affected = 0;
        bool anyChange = false;
        var dirtyScenes = new HashSet<UnityEngine.SceneManagement.Scene>();
        try
        {
            for (int i = 0; i < modules.Count; i++)
            {
                Transform module = modules[i];
                if (module == null) continue;

                EditorUtility.DisplayProgressBar(
                    mark ? "Marking module centers..." : "Unmarking module centers...",
                    module.name,
                    (i + 1f) / Mathf.Max(1, modules.Count));

                if (!TryFindCenterCubeRenderer(module, IgnoreBasePlaneDefault, IgnoreCombinedDefault, out Renderer centerRenderer))
                {
                    continue;
                }

                GameObject cube = centerRenderer.gameObject;
                if (mark)
                {
                    ManualCubeMarker marker = cube.GetComponent<ManualCubeMarker>();
                    if (marker == null)
                    {
                        marker = Undo.AddComponent<ManualCubeMarker>(cube);
                        // Apply immediately so user sees it without entering Play.
                        marker.Apply();
                        affected++;
                        anyChange = true;
                    }
                    else
                    {
                        marker.Apply();
                    }

                    // Also add the logical cue marker so gameplay/audio can detect it.
                    TurnCueMarker cue = cube.GetComponent<TurnCueMarker>();
                    if (cue == null)
                    {
                        Undo.AddComponent<TurnCueMarker>(cube);
                        anyChange = true;
                    }
                }
                else
                {
                    ManualCubeMarker marker = cube.GetComponent<ManualCubeMarker>();
                    if (marker != null)
                    {
                        marker.Clear();
                        Undo.DestroyObjectImmediate(marker);
                        affected++;
                        anyChange = true;
                    }
                    else
                    {
                        // If there is no ManualCubeMarker, still clear any leftover property block tint.
                        ClearRendererPropertyBlock(centerRenderer);
                    }

                    TurnCueMarker cue = cube.GetComponent<TurnCueMarker>();
                    if (cue != null)
                    {
                        Undo.DestroyObjectImmediate(cue);
                        anyChange = true;
                    }
                }

                // Ensure Unity serializes the modifications (scene/prefab instance).
                MarkObjectDirty(cube);
                if (cube.scene.IsValid()) dirtyScenes.Add(cube.scene);
                PrefabUtility.RecordPrefabInstancePropertyModifications(cube);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (anyChange)
        {
            foreach (var s in dirtyScenes)
            {
                if (s.IsValid()) EditorSceneManager.MarkSceneDirty(s);
            }
        }

        EditorUtility.DisplayDialog(
            "Module Center Markers",
            $"{(mark ? "Marked" : "Unmarked")} {affected} center cube(s).\n\n" +
            (anyChange ? "Scene marked as modified. Remember to SAVE THE SCENE (Ctrl+S) to persist changes.\n\n" : "") +
            "Note: If you used ModuleCenterHighlighter previously, this tool removes the marker component; " +
            "it also clears the renderer property block on unmarked cubes as a fallback.",
            "OK");
    }

    private static List<Transform> CollectModulesFromSelection(GameObject[] selected, string modulePrefix)
    {
        var modules = new List<Transform>(128);
        var seen = new HashSet<Transform>();

        // First pass: direct selected modules.
        for (int i = 0; i < selected.Length; i++)
        {
            if (selected[i] == null) continue;
            Transform t = selected[i].transform;
            if (t.name.StartsWith(modulePrefix) && seen.Add(t))
            {
                modules.Add(t);
            }
        }

        // If none selected directly, treat selection as roots and find descendants.
        if (modules.Count == 0)
        {
            for (int i = 0; i < selected.Length; i++)
            {
                if (selected[i] == null) continue;
                Transform root = selected[i].transform;
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in all)
                {
                    if (t == null || t == root) continue;
                    if (!t.name.StartsWith(modulePrefix)) continue;
                    if (seen.Add(t)) modules.Add(t);
                }
            }
        }

        return modules;
    }

    private static bool TryFindCenterCubeRenderer(Transform moduleRoot, bool ignoreBasePlane, bool ignoreCombined, out Renderer centerRenderer)
    {
        centerRenderer = null;
        if (moduleRoot == null) return false;

        // Compute bounds from eligible renderers.
        Renderer[] renderers = moduleRoot.GetComponentsInChildren<Renderer>(true);
        bool hasAny = false;
        Bounds bounds = default;

        foreach (Renderer r in renderers)
        {
            if (!ShouldConsider(r, ignoreBasePlane, ignoreCombined)) continue;
            if (!hasAny)
            {
                bounds = r.bounds;
                hasAny = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        if (!hasAny) return false;

        // Find closest renderer to bounds center.
        Vector3 target = bounds.center;
        float bestSqr = float.PositiveInfinity;
        foreach (Renderer r in renderers)
        {
            if (!ShouldConsider(r, ignoreBasePlane, ignoreCombined)) continue;
            float d = (r.transform.position - target).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                centerRenderer = r;
            }
        }

        return centerRenderer != null;
    }

    private static bool ShouldConsider(Renderer r, bool ignoreBasePlane, bool ignoreCombined)
    {
        if (r == null) return false;
        if (ignoreBasePlane && r.gameObject.name == "BasePlane") return false;
        if (ignoreCombined && r.gameObject.name.Contains("_Combined")) return false;
        return true;
    }

    private static void ClearRendererPropertyBlock(Renderer r)
    {
        if (r == null) return;
        var mpb = new MaterialPropertyBlock();
        // Clearing MPB reverts to material values.
        mpb.Clear();
        r.SetPropertyBlock(mpb);
    }

    private static void MarkObjectDirty(Object obj)
    {
        if (obj == null) return;
        EditorUtility.SetDirty(obj);
    }
}

