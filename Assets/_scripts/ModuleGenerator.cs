using System.Collections;
using System.Collections.Generic;
using UnityEngine;


//Creates the modules with cube so the path generator can go through
public class ModuleGenerator
{
    private readonly IMapGenerator _mapGenerator;
    private readonly IObjectPool _pool;
    private readonly Transform _rootParent;
    private int _totalModulesGenerated = 0; 

    public ModuleGenerator(IMapGenerator mapGenerator, IObjectPool pool, Transform rootParent)
    {
        this._mapGenerator = mapGenerator;
        this._pool = pool;
        this._rootParent = rootParent;
    }

    public IEnumerator StartRecursiveGeneration(int numModules)
    {
        yield return GenerateModuleRecursive(numModules);
    }

    private IEnumerator GenerateModuleRecursive(int numModules)
    {
        // Process modules continuously from queue until limit reached or queue is empty
        // This allows branches to be processed independently
        while (ModuleInfoQueueManager.Count > 0)
        {
            // If we've hit the module limit, only allow "blocker" modules (to seal exits).
        if (_totalModulesGenerated >= numModules)
        {
                ModuleInfo peek = ModuleInfoQueueManager.Peek();
                if (peek == null || !peek.IsBlocker)
                {
            yield break;
                }
            }

            // Optional: Hold Space to allow module processing; release to pause spawning new modules.
            // If GenerateInstantly is enabled, never pause.
            if (!MapGenerator3D.Instance.GenerateInstantly && MapGenerator3D.Instance.HoldSpaceToGenerate)
            {
                while (!Input.GetKey(KeyCode.Space))
                {
                    yield return null;
                }
        }

        // Generate the current module
        GameObject moduleContainer = new GameObject($"Module_{_totalModulesGenerated + 1}");
            if (_rootParent != null)
            {
                moduleContainer.transform.SetParent(_rootParent, true);
            }
            ModuleInfo myModuleInfo = ModuleInfoQueueManager.Dequeue();
            
            if (myModuleInfo == null)
        {
            yield break;
        }

            moduleContainer.transform.position = myModuleInfo.NextModulePosition;

            moduleContainer.transform.position = myModuleInfo.NextModulePosition;

            // Hook the spawned module root into the ModuleInfo so PathGenerator can tag it with metadata (turn/straight).
            myModuleInfo.RuntimeModuleRoot = moduleContainer.transform;

        // Generate layers
        // Top layer: walls/blocks. Needs colliders so the player camera can collide.
        GameObject[,] grassLayer = GenerateLayer(moduleContainer, _mapGenerator.GrassMaterial, 0, true);
        // Ground visuals (no colliders).
        GenerateLayer(moduleContainer, _mapGenerator.GroundMaterial, -1, false);
        // One invisible collider for the whole module floor.
        GenerateBasePlaneColliderOnly(moduleContainer, -1f);

            // Track path tiles for Voronoi preservation
            HashSet<Vector2Int> pathTiles = new HashSet<Vector2Int>();

            // 🏔️ Determinar si este módulo debe tener Voronoi (menos probable: cada 8 o 10 módulos)
            bool shouldApplyVoronoi = (_totalModulesGenerated % 8 == 0 || _totalModulesGenerated % 10 == 0);

            // Normal module: generate path. Blocker module: no path (full cubes).
            if (!myModuleInfo.IsBlocker)
            {
                // Generate the path asynchronously - cada módulo tiene su propio contexto independiente
                // Voronoi se aplicará gradualmente durante la generación del path si shouldApplyVoronoi es true
                yield return _mapGenerator.PathGenerator.GeneratePath(
                    grassLayer,
                    _totalModulesGenerated,
                    myModuleInfo,
                    pathTiles,
                    shouldApplyVoronoi,
                    myModuleInfo.NextModulePosition
                );
            }

        // Combine meshes
        MeshCombiner.CombineMeshesByMaterial(moduleContainer);

        // Increment the global counter
        _totalModulesGenerated++;

            // Wait for a frame before processing next module
            if (!MapGenerator3D.Instance.GenerateInstantly)
            {
        yield return null;
            }
        }

        if (_totalModulesGenerated >= numModules)
        {
        }
        else if (ModuleInfoQueueManager.Count == 0)
        {
        }
    }

    //Generates a layer of cubes. You can choose whether each cube collider is enabled.
    private GameObject[,] GenerateLayer(GameObject parent, Material material, float yOffset, bool enableColliders)
    {
        int mapWidth = _mapGenerator.MapWidth;
        int mapHeight = _mapGenerator.MapHeight;
        float spacing = _mapGenerator.Spacing;

        GameObject[,] layer = new GameObject[mapWidth, mapHeight];
        for (int x = 0; x < mapWidth; x++)
        {
            for (int z = 0; z < mapHeight; z++)
            {
                Vector3 position = new Vector3(x * spacing, yOffset, z * spacing) + parent.transform.position;
                GameObject cube = _pool.GetObject();
                cube.transform.position = position;
                cube.transform.rotation = Quaternion.identity;
                cube.transform.parent = parent.transform;
                // IMPORTANT: use sharedMaterial so all cubes truly share the same material reference.
                // Using renderer.material instantiates a unique material per cube, exploding draw calls and breaking mesh combining by material.
                Renderer r = cube.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = material;
                
                Collider c = cube.GetComponent<Collider>();
                if (enableColliders)
                {
                    if (c == null) c = cube.AddComponent<BoxCollider>();
                    c.enabled = true;
                    c.isTrigger = false;
                }
                else
                {
                    // Visual-only cubes: disable collider if present.
                    if (c != null) c.enabled = false;
                }
                layer[x, z] = cube;
            }
        }
        return layer;
    }

    // Generates a single invisible base plane (one collider) for the module bottom layer.
    private void GenerateBasePlaneColliderOnly(GameObject parent, float yCenter)
    {
        int mapWidth = _mapGenerator.MapWidth;
        int mapHeight = _mapGenerator.MapHeight;
        float spacing = _mapGenerator.Spacing;

        // Cover the same footprint as the grid: from (0..(w-1)*spacing) and (0..(h-1)*spacing)
        float sizeX = (mapWidth - 1) * spacing + 1f;
        float sizeZ = (mapHeight - 1) * spacing + 1f;

        Vector3 centerOffset = new Vector3((mapWidth - 1) * spacing * 0.5f, yCenter, (mapHeight - 1) * spacing * 0.5f);
        Vector3 position = parent.transform.position + centerOffset;

        GameObject baseCube = _pool.GetObject();
        baseCube.name = "BasePlane";
        baseCube.transform.SetParent(parent.transform, true);
        baseCube.transform.position = position;
        baseCube.transform.rotation = Quaternion.identity;
        baseCube.transform.localScale = new Vector3(sizeX, 1f, sizeZ);

        // Make it invisible: it's only for collision.
        Renderer r = baseCube.GetComponent<Renderer>();
        if (r != null) r.enabled = false;

        // Ensure it has exactly one collider enabled (the cube prefab usually has a BoxCollider).
        Collider c = baseCube.GetComponent<Collider>();
        if (c == null) c = baseCube.AddComponent<BoxCollider>();
        c.enabled = true;
        c.isTrigger = false;
        if (MapGenerator3D.Instance != null)
        {
            c.material = MapGenerator3D.Instance.GroundPhysicMaterial;
        }
    }
    
    /// <summary>
    /// Aplica Diagramas de Voronoi para crear zonas tipo cueva preservando los caminos
    /// </summary>
    private void ApplyVoronoiCaves(GameObject[,] grassLayer, HashSet<Vector2Int> pathTiles, Vector3 modulePosition)
    {
        int mapWidth = _mapGenerator.MapWidth;
        int mapHeight = _mapGenerator.MapHeight;
        
        // Obtener parámetros de Voronoi del MapGenerator
        int voronoiSeeds = _mapGenerator.VoronoiSeeds;
        float voronoiThreshold = _mapGenerator.VoronoiThreshold;
        float voronoiVariation = _mapGenerator.VoronoiVariation;
        bool useVoronoi = _mapGenerator.UseVoronoiCaves;
        
        if (!useVoronoi)
        {
            return; // Voronoi desactivado
        }
        
        // Crear generador de Voronoi con seed basado en la posición del módulo
        int moduleSeed = Mathf.RoundToInt(modulePosition.x * 1000 + modulePosition.z * 10000);
        VoronoiGenerator voronoi = new VoronoiGenerator(mapWidth, mapHeight, moduleSeed);
        
        // Generar máscara de cuevas usando Voronoi orgánico
        bool[,] caveMask = voronoi.GenerateOrganicCaveMask(
            voronoiSeeds, 
            voronoiThreshold, 
            voronoiVariation, 
            true, // Preservar caminos
            pathTiles
        );
        
        // Aplicar la máscara: eliminar cubos marcados como cueva
        for (int x = 0; x < mapWidth; x++)
        {
            for (int z = 0; z < mapHeight; z++)
            {
                if (caveMask[x, z] && grassLayer[x, z] != null)
                {
                    // Este tile es parte de una cueva, eliminarlo
                    _pool.ReturnObject(grassLayer[x, z]);
                    grassLayer[x, z] = null;
                }
            }
        }
    }
}
