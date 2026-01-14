using System.Collections;
using UnityEngine;


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
    public Vector2Int LastExit { get; set; }
    public CurrentDirection LastDirection { get; set; } = CurrentDirection.DOWN;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            pool = new ObjectPool(_cubePrefab, _chunkWidth * _chunkHeight * _numModules);
            pathGenerator = new PathGenerator(this, pool);    
            module = new ModuleGenerator(this, pool);
        }
    }



    void Start()
    {
        Vector3 modposCopy = new Vector3(nextModulePosition.x, nextModulePosition.y, nextModulePosition.z);
        CurrentDirection myLastDirection  = CurrentDirection.DOWN;
        Vector2Int initialExit = new Vector2Int(_chunkWidth / 2, _chunkHeight / 2);
        ModuleInfo myModuleInfo = new ModuleInfo(modposCopy, myLastDirection, initialExit);
        ModuleInfoQueueManager.Enqueue(myModuleInfo);
        Random.InitState(_seed);
        if (module == null)
        {  
            return;
        }
        StartCoroutine(GenerateModules());
    }
   
    //Start creating the modules
    private IEnumerator GenerateModules()
    {
        yield return StartCoroutine(module.StartRecursiveGeneration(_numModules));
    }

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

        // Calculate global module postion
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
            Debug.LogWarning($"⚠️ Module position {newModulePosition} is too close to existing module. Path cancelled - no module created.");
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




}
