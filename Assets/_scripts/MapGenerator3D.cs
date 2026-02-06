using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif


//main class keeps the info , inictiate the other clases and acts as a node of position for the modules and paths
public class MapGenerator3D : MonoBehaviour, IMapGenerator
{
    public static MapGenerator3D Instance { get; private set; }

    [Header("Map Configuration")]
    [SerializeField] private int _chunkWidth = 13;
    [SerializeField] private int _chunkHeight = 13;
    [Header("Seed Configuration")]
    [SerializeField] private int _seed = 0;
    [Header("Number of Modules Configuration")]
    [SerializeField] private int _numModules = 3;
    [Header("                                  ")]
    [SerializeField] private float _spacing = 1.2f;
    [SerializeField] private GameObject _cubePrefab;
    [SerializeField] private Material _groundMaterial;
    [SerializeField] private Material _grassMaterial;
    [SerializeField] private float _moduleSpacing = 1.2f;

    [Header("Voronoi Cave Configuration")]
    [SerializeField] private bool _useVoronoiCaves = true;
    [SerializeField] [Range(3, 15)] private int _voronoiSeeds = 8; // Más semillas = más zonas pequeñas
    [SerializeField] [Range(1f, 8f)] private float _voronoiThreshold = 2.0f; // Más pequeño = cuevas más pequeñas
    [SerializeField] [Range(0f, 3f)] private float _voronoiVariation = 0.3f; // Menos variación = más simétricas
    
    [Header("Voronoi Cave Shapes (percent)")]
    [Tooltip("Probability (%) that Voronoi caves use circular blobs.")]
    [SerializeField] [Range(0f, 100f)] private float _voronoiCirclePercent = 70f;
    [Tooltip("Probability (%) that Voronoi caves use square blobs.")]
    [SerializeField] [Range(0f, 100f)] private float _voronoiSquarePercent = 20f;
    [Tooltip("Probability (%) that Voronoi caves use cross/plus blobs.")]
    [SerializeField] [Range(0f, 100f)] private float _voronoiCrossPercent = 10f;
    [Tooltip("Cross arm thickness relative to the per-seed threshold. 0.25 means arms are 25% of the radius.")]
    [SerializeField] [Range(0.05f, 0.9f)] private float _voronoiCrossArmWidthFactor = 0.25f;
    
    [Header("Generation Control")]
    [Tooltip("If true, generation advances only while holding Space. If false, it runs automatically.")]
    [SerializeField] private bool _holdSpaceToGenerate = true;
    [Tooltip("If true, generate everything as fast as possible (no step delays / no per-module frame waits).")]
    [SerializeField] private bool _generateInstantly = false;

    [Header("Physics Materials")]
    [Tooltip("PhysicMaterial applied to the GROUND colliders (friction/bounce).")]
    [SerializeField] private PhysicMaterial _groundPhysicMaterial;

    [Header("Path Branching")]
    [Tooltip("Branches will start appearing from this module number (1-based). Example: 7 => branches start on Module 7 (moduleIndex 6).")]
    [SerializeField] private int _branchingStartsAtModuleNumber = 7;

    [Header("Export Modules To Prefabs (Editor)")]
    [Tooltip("Folder under Assets/ where module prefabs will be saved.")]
    [SerializeField] private string _modulePrefabExportFolder = "Assets/GeneratedModules";
    [Tooltip("If true, removes inactive children before saving (smaller prefabs if you have disabled objects).")]
    [SerializeField] private bool _exportStripInactiveChildren = true;
    [Tooltip("If true, export module prefabs automatically when generation finishes (Editor only).")]
    [SerializeField] private bool _autoExportModulePrefabsAfterGeneration = false;

    [Header("Export FULL MAP To Prefab (Editor)")]
    [Tooltip("Folder under Assets/ where the full generated map prefab will be saved.")]
    [SerializeField] private string _mapPrefabExportFolder = "Assets/Prefab/Maps";
    [Tooltip("If true, exports the full map prefab automatically when generation finishes (Editor only).")]
    [SerializeField] private bool _autoExportMapPrefabAfterGeneration = false;
    [Tooltip("If true, removes '*_Combined' mesh objects so the map stays editable per-cube.")]
    [SerializeField] private bool _mapExportRemoveCombinedMeshes = true;
    [Tooltip("If true, enables MeshRenderers on all cubes in modules (keeps BasePlane invisible).")]
    [SerializeField] private bool _mapExportEnableAllCubeRenderers = true;

    private ModuleGenerator module;
    private Vector3 nextModulePosition = Vector3.zero;
    private ObjectPool pool;
    private PathGenerator pathGenerator;
   
    public int MapWidth => _chunkWidth;
    public int MapHeight => _chunkHeight;
    public float Spacing =>_spacing;
    public float ModuleSpacing => _moduleSpacing;
    public PathGenerator PathGenerator => pathGenerator;
    public Vector3 NextModulePosition => nextModulePosition;
    public Material GroundMaterial => _groundMaterial;
    public Material GrassMaterial => _grassMaterial;
    public int NumModules => _numModules;
    public Vector2Int LastExit { get; set; }
    public CurrentDirection LastDirection { get; set; } = CurrentDirection.DOWN;
    
    // Voronoi properties
    public bool UseVoronoiCaves => _useVoronoiCaves;
    public int VoronoiSeeds => _voronoiSeeds;
    public float VoronoiThreshold => _voronoiThreshold;
    public float VoronoiVariation => _voronoiVariation;
    public float VoronoiCirclePercent => _voronoiCirclePercent;
    public float VoronoiSquarePercent => _voronoiSquarePercent;
    public float VoronoiCrossPercent => _voronoiCrossPercent;
    public float VoronoiCrossArmWidthFactor => _voronoiCrossArmWidthFactor;
    
    // Generation control (read by generators via MapGenerator3D.Instance)
    public bool HoldSpaceToGenerate => _holdSpaceToGenerate;
    public bool GenerateInstantly => _generateInstantly;
    public int BranchingStartsAtModuleNumber => _branchingStartsAtModuleNumber;
    public PhysicMaterial GroundPhysicMaterial => _groundPhysicMaterial;

    private Transform _mazeRoot;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            _mazeRoot = new GameObject("MazeRoot").transform;
            _mazeRoot.position = Vector3.zero;
            // +1 to allow a final "blocker" module without reallocations.
            pool = new ObjectPool(_cubePrefab, _chunkWidth * _chunkHeight * (_numModules + 1));
            pathGenerator = new PathGenerator(this, pool);    
            module = new ModuleGenerator(this, pool, _mazeRoot);
        }
    }



    void Start()
    {
        Vector3 modposCopy = new Vector3(nextModulePosition.x, nextModulePosition.y, nextModulePosition.z);
        CurrentDirection myLastDirection  = CurrentDirection.DOWN;
        Vector2Int initialExit = new Vector2Int(_chunkWidth / 2, _chunkHeight / 2);
        ModuleInfo myModuleInfo = new ModuleInfo(modposCopy, myLastDirection, initialExit);
        ModuleInfoQueueManager.Enqueue(myModuleInfo);

        PlaceMainCameraAtInitialPathTile(modposCopy);
        Random.InitState(_seed);
        if (module == null)
        {  
            return;
        }
        StartCoroutine(GenerateModules());
    }

    private void PlaceMainCameraAtInitialPathTile(Vector3 moduleOrigin)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // The path generator for module 0 starts at (MapWidth/2, MapHeight/2).
        int tileX = _chunkWidth / 2;
        int tileZ = _chunkHeight / 2;

        Vector3 target = moduleOrigin + new Vector3(tileX * _spacing, 0f, tileZ * _spacing);
        cam.transform.position = new Vector3(target.x, cam.transform.position.y, target.z);
    }
   
    //Start creating the modules
    private IEnumerator GenerateModules()
    {
        yield return StartCoroutine(module.StartRecursiveGeneration(_numModules));
#if UNITY_EDITOR
        if (_autoExportModulePrefabsAfterGeneration)
        {
            ExportGeneratedModulesToPrefabs();
        }
        if (_autoExportMapPrefabAfterGeneration)
        {
            ExportFullMapToPrefab();
        }
#endif
    }

#if UNITY_EDITOR
    public void ExportGeneratedModulesToPrefabs()
    {
        if (_mazeRoot == null) return;
        if (string.IsNullOrWhiteSpace(_modulePrefabExportFolder)) return;

        EnsureEditorFolder(_modulePrefabExportFolder);

        int exported = 0;
        for (int i = 0; i < _mazeRoot.childCount; i++)
        {
            Transform child = _mazeRoot.GetChild(i);
            if (child == null) continue;
            if (!child.name.StartsWith("Module_")) continue;

            string safeName = MakeSafeFileName(child.name);
            string prefabPath = Path.Combine(_modulePrefabExportFolder, $"{safeName}.prefab").Replace("\\", "/");

            GameObject clone = Instantiate(child.gameObject);
            clone.name = child.name;
            try
            {
                if (_exportStripInactiveChildren)
                {
                    StripInactiveChildrenRecursive(clone.transform);
                }

                PrefabUtility.SaveAsPrefabAsset(clone, prefabPath);
                exported++;
            }
            finally
            {
                DestroyImmediate(clone);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Exported {exported} module prefab(s) to '{_modulePrefabExportFolder}'.", this);
    }

    public void ExportFullMapToPrefab()
    {
        if (_mazeRoot == null) return;
        if (string.IsNullOrWhiteSpace(_mapPrefabExportFolder)) return;

        EnsureEditorFolder(_mapPrefabExportFolder);

        // Name uses seed/modules to be meaningful; GenerateUniqueAssetPath avoids overwriting.
        string baseName = $"Map_seed{_seed}_modules{_numModules}";
        string prefabPath = Path.Combine(_mapPrefabExportFolder, $"{MakeSafeFileName(baseName)}.prefab").Replace("\\", "/");
        prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);

        GameObject clone = Instantiate(_mazeRoot.gameObject);
        clone.name = "GeneratedMap";

        try
        {
            if (_mapExportRemoveCombinedMeshes)
            {
                RemoveCombinedMeshesRecursive(clone.transform);
            }

            if (_mapExportEnableAllCubeRenderers)
            {
                EnableAllMeshRenderersExceptBasePlane(clone.transform);
            }

            PrefabUtility.SaveAsPrefabAsset(clone, prefabPath);
        }
        finally
        {
            DestroyImmediate(clone);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"Exported full map prefab to '{prefabPath}'.", this);
    }

    private static void RemoveCombinedMeshesRecursive(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child == null) continue;

            // Remove any combined mesh objects created by MeshCombiner: "Module_X_MatName_Combined"
            if (child.name.Contains("_Combined"))
            {
                DestroyImmediate(child.gameObject);
                continue;
            }

            RemoveCombinedMeshesRecursive(child);
        }
    }

    private static void EnableAllMeshRenderersExceptBasePlane(Transform root)
    {
        MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer mr in mrs)
        {
            if (mr == null) continue;
            if (mr.gameObject.name == "BasePlane") continue;
            mr.enabled = true;
            if (!mr.gameObject.activeSelf) mr.gameObject.SetActive(true);
        }
    }

    private static void StripInactiveChildrenRecursive(Transform root)
    {
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

    private static void EnsureEditorFolder(string folderPath)
    {
        folderPath = folderPath.Replace("\\", "/").TrimEnd('/');
        if (AssetDatabase.IsValidFolder(folderPath)) return;

        if (!folderPath.StartsWith("Assets"))
        {
            throw new System.ArgumentException("Export folder must be under 'Assets/'. Example: Assets/GeneratedModules");
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
#endif

    //Node that gets info of the potsition oof the path and module and sets the new position for continuing the path
    public void DecideNextModulePosition(int exitX, int exitZ, CurrentDirection exitDirection)
    {
        DecideNextModulePosition(exitX, exitZ, exitDirection, null);
    }
    
    // Overload to allow creating module from a specific base position (for branches)
    public void DecideNextModulePosition(int exitX, int exitZ, CurrentDirection exitDirection, Vector3? basePosition)
    {
        float offsetX = _chunkWidth * _spacing;
        float offsetZ = _chunkHeight * _spacing;

        // Use provided base position or default to nextModulePosition
        Vector3 basePos = basePosition ?? nextModulePosition;

        // Calculate global module position
        Vector3 newModulePosition = exitDirection switch
        {
            CurrentDirection.DOWN => new Vector3(
                basePos.x,
                0,
                basePos.z + offsetZ + _moduleSpacing
            ),
            CurrentDirection.LEFT => new Vector3(
                basePos.x - offsetX - _moduleSpacing,
                0,
                basePos.z
            ),
            CurrentDirection.RIGHT => new Vector3(
                basePos.x + offsetX + _moduleSpacing,
                0,
                basePos.z
            ),
            _ => basePos
        };
      
        // ⚠️ VALIDACIÓN DE PROXIMIDAD: Usar el mismo sistema que previene solapamientos dentro del módulo
        // Calcular distancia mínima entre módulos (aproximadamente el tamaño de un módulo)
        float minModuleDistance = Mathf.Min(offsetX, offsetZ) * 0.8f; // 80% del tamaño del módulo
        
        if (ModuleInfoQueueManager.IsPositionTooClose(newModulePosition, minModuleDistance))
        {
            return; // ❌ CANCELAR: No crear módulo si está demasiado cerca
        }
      
        Vector3 modposCopy = new Vector3(newModulePosition.x, newModulePosition.y, newModulePosition.z);
        CurrentDirection myCurrentDirection = exitDirection;

        // Calculate the entry point for the next module based on exit direction
        Vector2Int entryExit = exitDirection switch
        {
            CurrentDirection.DOWN => new Vector2Int(exitX, 0),
            CurrentDirection.LEFT => new Vector2Int(_chunkWidth - 1, exitZ),
            CurrentDirection.RIGHT => new Vector2Int(0, exitZ),
            _ => new Vector2Int(exitX, exitZ)
        };

        ModuleInfo myModuleInfo = new ModuleInfo(modposCopy, myCurrentDirection, entryExit);
        ModuleInfoQueueManager.Enqueue(myModuleInfo);

        // Only update global state if this is the main path (not a branch)
        if (basePosition == null)
        {
            nextModulePosition = newModulePosition;
            
            //Updates the last exit of the path so it knows where to continue on the nex module
            LastExit = entryExit;
        }
    }

    // Enqueue a full module with NO path generation (used to seal the final exit).
    public void EnqueueBlockerModuleAtExit(int exitX, int exitZ, CurrentDirection exitDirection, Vector3 currentModulePosition)
    {
        float offsetX = _chunkWidth * _spacing;
        float offsetZ = _chunkHeight * _spacing;

        Vector3 basePos = currentModulePosition;

        Vector3 newModulePosition = exitDirection switch
        {
            CurrentDirection.DOWN => new Vector3(basePos.x, 0, basePos.z + offsetZ + _moduleSpacing),
            CurrentDirection.LEFT => new Vector3(basePos.x - offsetX - _moduleSpacing, 0, basePos.z),
            CurrentDirection.RIGHT => new Vector3(basePos.x + offsetX + _moduleSpacing, 0, basePos.z),
            _ => basePos
        };

        float minModuleDistance = Mathf.Min(offsetX, offsetZ) * 0.8f;
        if (ModuleInfoQueueManager.IsPositionTooClose(newModulePosition, minModuleDistance))
        {
            return;
        }

        Vector2Int entryExit = exitDirection switch
        {
            CurrentDirection.DOWN => new Vector2Int(exitX, 0),
            CurrentDirection.LEFT => new Vector2Int(_chunkWidth - 1, exitZ),
            CurrentDirection.RIGHT => new Vector2Int(0, exitZ),
            _ => new Vector2Int(exitX, exitZ)
        };

        ModuleInfo blocker = new ModuleInfo(newModulePosition, exitDirection, entryExit, true);
        ModuleInfoQueueManager.Enqueue(blocker);
    }

}
