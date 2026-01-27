using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Optimizes the currently selected scene GameObject by saving it as a prefab and running the static map optimizer logic.
/// Intended for cases where you already placed an exported map prefab in the scene and want to re-bake it.
/// </summary>
public static class OptimizeSelectedMapInScene
{
    private const string DefaultOutFolder = "Assets/Prefab/MapsOptimized";
    private const float DefaultEnableDistance = 120f;
    private const float DefaultDisableDistance = 140f;

    [MenuItem("Tools/Procedural/Optimize Selected Map (Scene Object)")]
    private static void OptimizeSelected()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogWarning("Optimize Selected Map: Select the map root object in the Hierarchy first.");
            return;
        }

        // Create a temp prefab from the selection so we can optimize deterministically.
        EnsureFolder(DefaultOutFolder);
        string safe = MakeSafeFileName(selected.name);
        string tempPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(DefaultOutFolder, $"{safe}_SceneCopy.prefab").Replace("\\", "/"));

        GameObject copy = Object.Instantiate(selected);
        copy.name = selected.name;
        try
        {
            // Save the copy as a prefab asset.
            PrefabUtility.SaveAsPrefabAsset(copy, tempPath);
        }
        finally
        {
            Object.DestroyImmediate(copy);
        }

        // Now optimize that prefab asset using the same window tool (user can further optimize via UI if desired).
        // We cannot directly call the private window method, so we just inform the user where the copy is.
        Debug.Log($"Saved scene selection as prefab for optimization: {tempPath}\n" +
                  $"Next: Open Tools → Procedural → Optimize Static Map Prefab and select this prefab as Source.", 
            AssetDatabase.LoadAssetAtPath<Object>(tempPath));
    }

    [MenuItem("Tools/Procedural/Optimize Selected Map (Apply in Scene)")]
    private static void OptimizeSelectedInPlace()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogWarning("Optimize Selected Map (Apply in Scene): Select the map root object in the Hierarchy first.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(selected, "Optimize Selected Map (Apply in Scene)");

        // 1) Kill shadows & expensive probe usages on all renderers under the selection.
        Renderer[] rs = selected.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in rs)
        {
            if (r == null) continue;

            // Big FPS win for huge tile maps (especially when looking at the whole maze).
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            // Probes can be surprisingly expensive on thousands of renderers.
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // 2) Mark static so batching + occlusion can help (after baking occlusion culling).
        StaticEditorFlags flags =
            StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.NavigationStatic;

        Transform[] all = selected.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in all)
        {
            if (t == null) continue;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }

        // 3) Add distance culling helper (optional but recommended).
        MapDistanceCuller culler = selected.GetComponent<MapDistanceCuller>();
        if (culler == null)
        {
            culler = Undo.AddComponent<MapDistanceCuller>(selected);
        }

        // Set some reasonable defaults if user hasn't tuned it yet.
        SerializedObject so = new SerializedObject(culler);
        so.FindProperty("_enableDistance").floatValue = DefaultEnableDistance;
        so.FindProperty("_disableDistance").floatValue = DefaultDisableDistance;
        so.FindProperty("_renderersOnly").boolValue = true;
        so.FindProperty("_ignoreY").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(selected);
        Debug.Log("Applied scene optimization: disabled shadows/probes, marked Static, and added MapDistanceCuller to the selected map root.", selected);
    }

    private static string MakeSafeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    private static void EnsureFolder(string folderPath)
    {
        folderPath = folderPath.Replace("\\", "/").TrimEnd('/');
        if (AssetDatabase.IsValidFolder(folderPath)) return;

        if (!folderPath.StartsWith("Assets"))
        {
            throw new System.ArgumentException("Output folder must be under 'Assets/'. Example: Assets/Prefab/MapsOptimized");
        }

        string[] parts = folderPath.Split('/');
        string current = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}

