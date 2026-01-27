using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MapGenerator3D))]
public class MapGenerator3DEditor : Editor
{
    private SerializedProperty _modulePrefabExportFolder;
    private SerializedProperty _exportStripInactiveChildren;
    private SerializedProperty _autoExportModulePrefabsAfterGeneration;
    private SerializedProperty _mapPrefabExportFolder;
    private SerializedProperty _autoExportMapPrefabAfterGeneration;
    private SerializedProperty _mapExportRemoveCombinedMeshes;
    private SerializedProperty _mapExportEnableAllCubeRenderers;

    private void OnEnable()
    {
        _modulePrefabExportFolder = serializedObject.FindProperty("_modulePrefabExportFolder");
        _exportStripInactiveChildren = serializedObject.FindProperty("_exportStripInactiveChildren");
        _autoExportModulePrefabsAfterGeneration = serializedObject.FindProperty("_autoExportModulePrefabsAfterGeneration");

        _mapPrefabExportFolder = serializedObject.FindProperty("_mapPrefabExportFolder");
        _autoExportMapPrefabAfterGeneration = serializedObject.FindProperty("_autoExportMapPrefabAfterGeneration");
        _mapExportRemoveCombinedMeshes = serializedObject.FindProperty("_mapExportRemoveCombinedMeshes");
        _mapExportEnableAllCubeRenderers = serializedObject.FindProperty("_mapExportEnableAllCubeRenderers");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Export Modules To Prefabs (Editor)", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(_modulePrefabExportFolder, new GUIContent("Export Folder"));
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                string current = _modulePrefabExportFolder.stringValue;
                string start = Application.dataPath;
                if (!string.IsNullOrWhiteSpace(current) && current.StartsWith("Assets/"))
                {
                    start = Path.GetFullPath(Path.Combine(Application.dataPath, "..", current));
                }

                string picked = EditorUtility.OpenFolderPanel("Select export folder (must be under Assets/)", start, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    string full = Path.GetFullPath(picked);
                    if (full.StartsWith(projectRoot))
                    {
                        string rel = full.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            .Replace("\\", "/");
                        if (!rel.StartsWith("Assets/") && rel != "Assets")
                        {
                            EditorUtility.DisplayDialog("Invalid folder", "Please choose a folder under Assets/.", "OK");
                        }
                        else
                        {
                            _modulePrefabExportFolder.stringValue = rel;
                        }
                    }
                }
            }
        }

        EditorGUILayout.PropertyField(_exportStripInactiveChildren, new GUIContent("Strip Inactive Children"));
        EditorGUILayout.PropertyField(_autoExportModulePrefabsAfterGeneration, new GUIContent("Auto Export After Generation"));

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Export Generated Modules Now"))
            {
                serializedObject.ApplyModifiedProperties();
                MapGenerator3D gen = (MapGenerator3D)target;
                gen.ExportGeneratedModulesToPrefabs();
            }
        }

        EditorGUILayout.HelpBox(
            "To export prefabs:\n" +
            "- Run Play Mode and generate modules (MazeRoot/Module_*).\n" +
            "- Click 'Export Generated Modules Now' (enabled in Play Mode).\n" +
            "- Prefabs are saved under the chosen folder.",
            MessageType.Info);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Export FULL MAP To Prefab (Editor)", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(_mapPrefabExportFolder, new GUIContent("Map Export Folder"));
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                string current = _mapPrefabExportFolder.stringValue;
                string start = Application.dataPath;
                if (!string.IsNullOrWhiteSpace(current) && current.StartsWith("Assets/"))
                {
                    start = Path.GetFullPath(Path.Combine(Application.dataPath, "..", current));
                }

                string picked = EditorUtility.OpenFolderPanel("Select export folder (must be under Assets/)", start, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    string full = Path.GetFullPath(picked);
                    if (full.StartsWith(projectRoot))
                    {
                        string rel = full.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            .Replace("\\", "/");
                        if (!rel.StartsWith("Assets/") && rel != "Assets")
                        {
                            EditorUtility.DisplayDialog("Invalid folder", "Please choose a folder under Assets/.", "OK");
                        }
                        else
                        {
                            _mapPrefabExportFolder.stringValue = rel;
                        }
                    }
                }
            }
        }

        EditorGUILayout.PropertyField(_mapExportRemoveCombinedMeshes, new GUIContent("Remove *_Combined meshes"));
        EditorGUILayout.PropertyField(_mapExportEnableAllCubeRenderers, new GUIContent("Enable all cube MeshRenderers"));
        EditorGUILayout.PropertyField(_autoExportMapPrefabAfterGeneration, new GUIContent("Auto Export Map After Generation"));

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Export FULL MAP Prefab Now"))
            {
                serializedObject.ApplyModifiedProperties();
                MapGenerator3D gen = (MapGenerator3D)target;
                gen.ExportFullMapToPrefab();
            }
        }

        EditorGUILayout.HelpBox(
            "Full map export:\n" +
            "- Creates ONE prefab containing all generated modules.\n" +
            "- Optionally removes '*_Combined' objects so the prefab is editable per-cube.\n" +
            "- Optionally enables all cube MeshRenderers (BasePlane stays invisible).",
            MessageType.Info);

        serializedObject.ApplyModifiedProperties();
    }
}

