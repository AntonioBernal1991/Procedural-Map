using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class ModulePrefabBakerWindow : EditorWindow
{
    private const string DefaultOutputFolder = "Assets/GeneratedModules";

    private string _outputFolder = DefaultOutputFolder;
    private bool _stripInactiveChildren = true;

    [MenuItem("Tools/Procedural/Module Prefab Baker")]
    public static void ShowWindow()
    {
        GetWindow<ModulePrefabBakerWindow>("Module Prefab Baker");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Bakes generated module GameObjects into Prefab assets.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space();

        _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);
        _stripInactiveChildren = EditorGUILayout.ToggleLeft("Strip inactive children (reduces prefab size)", _stripInactiveChildren);

        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Bake Selected GameObject"))
            {
                BakeSelected();
            }

            if (GUILayout.Button("Bake All Modules Under MazeRoot"))
            {
                BakeAllUnderMazeRoot();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Usage:\n" +
            "- Run your generator so it creates module GameObjects (e.g. MazeRoot/Module_1, Module_2, ...).\n" +
            "- Open this window and click Bake.\n\n" +
            "Tip: Ensure your module containers are separate GameObjects (they already are: Module_*).",
            MessageType.Info);
    }

    private void BakeSelected()
    {
        GameObject go = Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("Module Prefab Baker", "Select a module GameObject in the Hierarchy first.", "OK");
            return;
        }

        EnsureFolder(_outputFolder);

        string safeName = MakeSafeFileName(go.name);
        string path = Path.Combine(_outputFolder, $"{safeName}.prefab").Replace("\\", "/");

        BakeOne(go, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void BakeAllUnderMazeRoot()
    {
        GameObject mazeRoot = GameObject.Find("MazeRoot");
        if (mazeRoot == null)
        {
            EditorUtility.DisplayDialog("Module Prefab Baker", "Could not find a GameObject named 'MazeRoot' in the scene.", "OK");
            return;
        }

        Transform[] modules = mazeRoot
            .GetComponentsInChildren<Transform>(true)
            .Where(t => t.parent == mazeRoot.transform && t.name.StartsWith("Module_"))
            .ToArray();

        if (modules.Length == 0)
        {
            EditorUtility.DisplayDialog("Module Prefab Baker", "No children named 'Module_*' found under MazeRoot.", "OK");
            return;
        }

        EnsureFolder(_outputFolder);

        foreach (Transform t in modules)
        {
            string safeName = MakeSafeFileName(t.gameObject.name);
            string path = Path.Combine(_outputFolder, $"{safeName}.prefab").Replace("\\", "/");
            BakeOne(t.gameObject, path);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void BakeOne(GameObject source, string prefabPath)
    {
        // Create a temporary clone so we can optionally strip children and avoid modifying the scene object.
        GameObject clone = Instantiate(source);
        clone.name = source.name;

        try
        {
            if (_stripInactiveChildren)
            {
                StripInactiveChildrenRecursive(clone.transform);
            }

            PrefabUtility.SaveAsPrefabAsset(clone, prefabPath);
            Debug.Log($"Saved module prefab: {prefabPath}", AssetDatabase.LoadAssetAtPath<Object>(prefabPath));
        }
        finally
        {
            DestroyImmediate(clone);
        }
    }

    private static void StripInactiveChildrenRecursive(Transform root)
    {
        // Depth-first, delete inactive children.
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (!child.gameObject.activeSelf)
            {
                DestroyImmediate(child.gameObject);
                continue;
            }
            StripInactiveChildrenRecursive(child);
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

        // Create nested folders under Assets.
        if (!folderPath.StartsWith("Assets"))
        {
            throw new System.ArgumentException("Output folder must be under 'Assets/'. Example: Assets/GeneratedModules");
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

