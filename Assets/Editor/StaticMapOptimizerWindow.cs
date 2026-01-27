using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class StaticMapOptimizerWindow : EditorWindow
{
    private GameObject _sourceMapPrefab;

    private string _outputFolder = "Assets/Prefab/MapsOptimized";
    private bool _processEachModule = true;
    private bool _deleteOriginalMeshes = true;
    private bool _addMeshColliders = true;
    private bool _markStatic = true;
    private bool _normalizeMaterials = true;
    private bool _disableShadows = true;

    [MenuItem("Tools/Procedural/Optimize Static Map Prefab")]
    public static void ShowWindow()
    {
        GetWindow<StaticMapOptimizerWindow>("Static Map Optimizer");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Creates an optimized version of a static map prefab:", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space();

        _sourceMapPrefab = (GameObject)EditorGUILayout.ObjectField("Source Map Prefab", _sourceMapPrefab, typeof(GameObject), false);
        _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);

        EditorGUILayout.Space();
        _processEachModule = EditorGUILayout.ToggleLeft("Process each Module_* separately (recommended)", _processEachModule);
        _deleteOriginalMeshes = EditorGUILayout.ToggleLeft("Delete original cube mesh objects after combining (big memory win)", _deleteOriginalMeshes);
        _addMeshColliders = EditorGUILayout.ToggleLeft("Add MeshColliders to combined meshes (keeps collision)", _addMeshColliders);
        _markStatic = EditorGUILayout.ToggleLeft("Mark optimized prefab as Static", _markStatic);
        _normalizeMaterials = EditorGUILayout.ToggleLeft("Normalize materials (fix per-cube instances before combining)", _normalizeMaterials);
        _disableShadows = EditorGUILayout.ToggleLeft("Disable shadows on combined meshes (big FPS win)", _disableShadows);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Notes:\n" +
            "- This tool is Editor-only.\n" +
            "- It combines meshes by material, then optionally deletes the original cube mesh objects.\n" +
            "- 'Normalize materials' is important if your prefab was exported while cubes used renderer.material (material instances).\n" +
            "- If you delete originals, enable MeshColliders so the player still collides with walls.\n" +
            "- BasePlane renderers are kept disabled; their colliders remain untouched.\n",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(_sourceMapPrefab == null))
        {
            if (GUILayout.Button("Create Optimized Prefab"))
            {
                CreateOptimizedPrefab();
            }
        }
    }

    private void CreateOptimizedPrefab()
    {
        string srcPath = AssetDatabase.GetAssetPath(_sourceMapPrefab);
        if (string.IsNullOrWhiteSpace(srcPath) || !srcPath.EndsWith(".prefab"))
        {
            EditorUtility.DisplayDialog("Static Map Optimizer", "Please assign a prefab asset as Source Map Prefab.", "OK");
            return;
        }

        EnsureFolder(_outputFolder);

        string srcName = Path.GetFileNameWithoutExtension(srcPath);
        string outPath = Path.Combine(_outputFolder, $"{MakeSafeFileName(srcName)}_Optimized.prefab").Replace("\\", "/");
        outPath = AssetDatabase.GenerateUniqueAssetPath(outPath);

        GameObject root = PrefabUtility.LoadPrefabContents(srcPath);
        if (root == null)
        {
            EditorUtility.DisplayDialog("Static Map Optimizer", "Failed to load prefab contents.", "OK");
            return;
        }

        try
        {
            if (_normalizeMaterials)
            {
                NormalizeMaterialsInPrefab(root.transform);
            }

            if (_processEachModule)
            {
                Transform[] modules = root.transform
                    .Cast<Transform>()
                    .Where(t => t != null && t.name.StartsWith("Module_"))
                    .ToArray();

                if (modules.Length == 0)
                {
                    // Fall back to processing the whole prefab.
                    CombineAndReplace(root.transform);
                }
                else
                {
                    foreach (Transform m in modules)
                    {
                        CombineAndReplace(m);
                    }
                }
            }
            else
            {
                CombineAndReplace(root.transform);
            }

            if (_markStatic)
            {
                MarkStaticRecursive(root);
            }

            PrefabUtility.SaveAsPrefabAsset(root, outPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"Created optimized map prefab: {outPath}", AssetDatabase.LoadAssetAtPath<Object>(outPath));
    }

    private void CombineAndReplace(Transform scope)
    {
        // Collect all mesh filters except BasePlane and already combined meshes.
        MeshFilter[] meshFilters = scope.GetComponentsInChildren<MeshFilter>(true);
        var candidates = new List<MeshFilter>(meshFilters.Length);
        foreach (MeshFilter mf in meshFilters)
        {
            if (mf == null || mf.sharedMesh == null) continue;
            string n = mf.gameObject.name;
            if (n == "BasePlane") continue;
            if (n.Contains("_Combined")) continue;

            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null) continue;

            candidates.Add(mf);
        }

        if (candidates.Count <= 1)
        {
            // Nothing to combine.
            return;
        }

        // Group by material.
        var groups = new Dictionary<Material, List<MeshFilter>>();
        foreach (MeshFilter mf in candidates)
        {
            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            Material mat = mr.sharedMaterial;
            if (!groups.TryGetValue(mat, out var list))
            {
                list = new List<MeshFilter>();
                groups[mat] = list;
            }
            list.Add(mf);
        }

        // Create combined per material.
        foreach (var kv in groups)
        {
            Material mat = kv.Key;
            List<MeshFilter> list = kv.Value;
            if (list.Count <= 1) continue;

            var combine = new List<CombineInstance>(list.Count);
            foreach (MeshFilter mf in list)
            {
                combine.Add(new CombineInstance
                {
                    mesh = mf.sharedMesh,
                    transform = scope.worldToLocalMatrix * mf.transform.localToWorldMatrix
                });
            }

            GameObject combinedGo = new GameObject($"{scope.name}_{mat.name}_Combined");
            combinedGo.transform.SetParent(scope, false);
            combinedGo.transform.localPosition = Vector3.zero;
            combinedGo.transform.localRotation = Quaternion.identity;

            MeshFilter cmf = combinedGo.AddComponent<MeshFilter>();
            MeshRenderer cmr = combinedGo.AddComponent<MeshRenderer>();
            cmr.sharedMaterial = mat;
            if (_disableShadows)
            {
                cmr.shadowCastingMode = ShadowCastingMode.Off;
                cmr.receiveShadows = false;
            }

            Mesh mesh = new Mesh();
            mesh.name = $"{combinedGo.name}_Mesh";
            mesh.CombineMeshes(combine.ToArray(), true, true);
            cmf.sharedMesh = mesh;

            if (_addMeshColliders)
            {
                MeshCollider mc = combinedGo.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = false;
                mc.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning;
            }
        }

        // Delete or disable originals.
        if (_deleteOriginalMeshes)
        {
            foreach (MeshFilter mf in candidates)
            {
                if (mf == null) continue;
                DestroyImmediate(mf.gameObject);
            }
        }
        else
        {
            foreach (MeshFilter mf in candidates)
            {
                if (mf == null) continue;
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;
            }
        }
    }

    private static void MarkStaticRecursive(GameObject root)
    {
        StaticEditorFlags flags =
            StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.NavigationStatic;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in all)
        {
            if (t == null) continue;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }
    }

    private static void NormalizeMaterialsInPrefab(Transform root)
    {
        // Converts per-instance materials (often created via renderer.material) back to shared asset materials.
        // This is required so "group by material" actually groups cubes together.
        var cache = new Dictionary<string, Material>();
        MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer mr in mrs)
        {
            if (mr == null) continue;
            if (mr.gameObject.name == "BasePlane") continue;

            Material m = mr.sharedMaterial;
            if (m == null) continue;

            // If this is already a real asset material, leave it.
            if (AssetDatabase.Contains(m)) continue;

            string key = $"{(m.shader != null ? m.shader.name : "null")}::{m.name}";
            if (cache.TryGetValue(key, out var cached) && cached != null)
            {
                mr.sharedMaterial = cached;
                continue;
            }

            string targetName = m.name.Replace(" (Instance)", "");
            string[] guids = AssetDatabase.FindAssets($"t:Material {targetName}");
            Material best = null;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material candidate = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (candidate == null) continue;
                if (!candidate.name.Equals(targetName)) continue;
                if (m.shader != null && candidate.shader != null && candidate.shader.name != m.shader.name) continue;
                best = candidate;
                break;
            }

            if (best != null)
            {
                mr.sharedMaterial = best;
                cache[key] = best;
            }
        }
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

