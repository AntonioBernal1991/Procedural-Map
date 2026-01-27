using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class EnableMeshRenderersInModulePrefabs
{
    private const string DefaultFolder = "Assets/Prefab/Modules";

    [MenuItem("Tools/Procedural/Prefabs/Enable MeshRenderers in Assets/Prefab/Modules")]
    public static void EnableInDefaultFolder()
    {
        EnableInFolder(DefaultFolder);
    }

    private static void EnableInFolder(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder))
        {
            EditorUtility.DisplayDialog(
                "Enable MeshRenderers",
                $"Folder not found:\n{folder}\n\nMake sure it exists under Assets/.",
                "OK");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        int prefabsTouched = 0;
        int renderersEnabled = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(path)) continue;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) continue;

                try
                {
                    bool changed = false;
                    MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(true);
                    foreach (MeshRenderer mr in mrs)
                    {
                        if (mr == null) continue;

                        // Keep our invisible floor collider plane invisible.
                        if (mr.gameObject.name == "BasePlane") continue;

                        if (!mr.enabled)
                        {
                            mr.enabled = true;
                            renderersEnabled++;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        prefabsTouched++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"Enabled {renderersEnabled} MeshRenderer(s) across {prefabsTouched} prefab(s) in '{folder}'.");
    }
}

