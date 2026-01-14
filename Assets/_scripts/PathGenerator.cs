using System.Collections;
using UnityEngine;
using System.Collections.Generic;




//Generates Paths throuhg the modules cubes
public class PathGenerator 
{
    private readonly IMapGenerator _mapGenerator;
    private readonly IObjectPool _pool;
    private int _mapWidth;
    private int _mapHeight;
    private int _baseSeed;

    public  PathGenerator(IMapGenerator mapGenerator, IObjectPool pool)
    {
        this._mapGenerator = mapGenerator;
        this._pool = pool;
        this._mapWidth = mapGenerator.MapWidth;
        this._mapHeight = mapGenerator.MapHeight;
    }
    
    // Context class that holds all state for a single path generation
    // Each module gets its own independent "brain"
    private class PathGenerationContext
    {
        public System.Random Random { get; }
        public bool IsRepeating { get; set; }
        public int ModuleSeed { get; }
        
        // Normaliza la posición usando moduleSpacing = 1.2 fijo para el seed
        // Esto asegura que las direcciones sean siempre las mismas independientemente del moduleSpacing real
        private static float NORMALIZED_MODULE_SPACING = 1.2f;
        
        public PathGenerationContext(int baseSeed, Vector3 modulePosition, float currentModuleSpacing, float offsetX, float offsetZ)
        {
            // Normalizar la posición como si moduleSpacing fuera siempre 1.2
            Vector3 normalizedPosition = NormalizeModulePosition(modulePosition, currentModuleSpacing, NORMALIZED_MODULE_SPACING, offsetX, offsetZ);
            
            // Create unique seed for this module based on normalized position
            ModuleSeed = Mathf.RoundToInt(normalizedPosition.x * 1000 + normalizedPosition.z * 10000);
            Random = new System.Random(baseSeed + ModuleSeed);
            IsRepeating = false;
        }
        
        // Normaliza la posición del módulo para que use el moduleSpacing fijo
        private Vector3 NormalizeModulePosition(Vector3 actualPosition, float actualSpacing, float normalizedSpacing, float offsetX, float offsetZ)
        {
            // Si el spacing es el mismo, no hay que normalizar
            if (Mathf.Approximately(actualSpacing, normalizedSpacing))
            {
                return actualPosition;
            }
            
            // Calcular el desplazamiento por módulo en cada dirección
            float actualStepX = offsetX + actualSpacing;
            float actualStepZ = offsetZ + actualSpacing;
            float normalizedStepX = offsetX + normalizedSpacing;
            float normalizedStepZ = offsetZ + normalizedSpacing;
            
            // Calcular cuántos módulos se han generado en cada dirección desde el origen (0,0,0)
            // Usar un umbral pequeño para manejar errores de punto flotante
            float threshold = 0.1f;
            
            // Para X: puede ser positivo (RIGHT) o negativo (LEFT)
            int stepsX = 0;
            if (Mathf.Abs(actualPosition.x) > threshold)
            {
                stepsX = Mathf.RoundToInt(actualPosition.x / actualStepX);
            }
            
            // Para Z: generalmente positivo (DOWN)
            int stepsZ = 0;
            if (Mathf.Abs(actualPosition.z) > threshold)
            {
                stepsZ = Mathf.RoundToInt(actualPosition.z / actualStepZ);
            }
            
            // Recalcular la posición usando el spacing normalizado
            Vector3 normalizedPos = new Vector3(
                stepsX * normalizedStepX,
                0,
                stepsZ * normalizedStepZ
            );
            
            return normalizedPos;
        }
    }


        public IEnumerator GeneratePath(GameObject[,] cubes, int moduleIndex, ModuleInfo moduleInfo)
    {
        Debug.Log("module index - " + moduleIndex + " pos: " + moduleInfo.NextModulePosition + " entry: " + moduleInfo.LastExit + " dir: " + moduleInfo.LastDirection);
        
        // Create independent context for this module - its own "brain"
        // Use a consistent base seed (from Unity's Random state) plus module variation
        int baseSeed = Random.state.GetHashCode();
        float offsetX = _mapGenerator.MapWidth * _mapGenerator.Spacing;
        float offsetZ = _mapGenerator.MapHeight * _mapGenerator.Spacing;
        PathGenerationContext context = new PathGenerationContext(
            baseSeed, 
            moduleInfo.NextModulePosition, 
            _mapGenerator.ModuleSpacing,
            offsetX,
            offsetZ
        );
        
        // Use ModuleInfo to get the entry point and direction for this specific module
        Vector2Int lastExit = moduleIndex == 0 
            ? new Vector2Int(_mapWidth / 2, _mapHeight / 2)
            : moduleInfo.LastExit;
        CurrentDirection curDirection = moduleIndex == 0
            ? CurrentDirection.DOWN
            : moduleInfo.LastDirection;
        CurrentDirection origDirection = curDirection;
        HashSet<CurrentDirection> usedDirections = new HashSet<CurrentDirection> { curDirection };
        
        // Store the module info reference to use when finalizing path
        ModuleInfo currentModuleInfo = moduleInfo;

        int curX = lastExit.x;
        int curZ = lastExit.y;
        int maxSteps = _mapWidth + _mapHeight;
        
        // Track used positions in this module to prevent path overlaps
        HashSet<Vector2Int> usedPositionsInModule = new HashSet<Vector2Int>();
        
        // If entering from a side, continue in that direction at least until we move from entry point
        bool hasMovedFromEntry = (curDirection == CurrentDirection.DOWN);

        for (int step = 0; step < maxSteps; step++)
        {
            Vector2Int currentPos = new Vector2Int(curX, curZ);
            
            // Check if this position has already been used (prevent path overlaps)
            if (usedPositionsInModule.Contains(currentPos))
            {
                Debug.LogWarning($"Path position {currentPos} already used in module. Skipping to prevent overlap.");
                // Try to find alternative direction or break
                break;
            }
            
            usedPositionsInModule.Add(currentPos);
            ClearTile(cubes, curX, curZ);

            // Only allow direction changes at center if we've moved from entry point
            // This ensures side entries continue in their direction initially
            if (IsAtCenter(curX, curZ) && hasMovedFromEntry)
            {
                curDirection = DetermineNextDirection(curX, curZ, curDirection, usedDirections, context);

                // Reset direction history if all directions are used
                if (usedDirections.Count == 3)
                {
                    ResetDirectionHistory(usedDirections, curDirection);
                }
            }
            
            // Mark that we've moved from entry point after first move
            if (!hasMovedFromEntry)
            {
                hasMovedFromEntry = true;
            }
            
            //Creates new branch when there is a turn and a probabilty
            if (curDirection == CurrentDirection.LEFT || curDirection == CurrentDirection.RIGHT)
            {
                CurrentDirection branchDirection = GetBranchDirection(curDirection, origDirection);

                if (moduleIndex % 3 == 0 && moduleIndex != 0)
                {
                    bool isBranch = ThreeUniqueDirections(curDirection, origDirection, branchDirection);
                    
                    if (isBranch)
                    {
                        // When bifurcating, save information for BOTH paths independently
                        Vector3 currentModulePosition = moduleInfo.NextModulePosition;
                        
                        // Pre-create module info for the branch path immediately
                        // This ensures the branch has its own independent state
                        PreCreateBranchModuleInfo(curX, curZ, branchDirection, currentModulePosition);
                        
                        // Generate the visual branch
                        CoroutineManager.Instance.StartManagedCoroutine(GenerateBranch(cubes, curX, curZ, branchDirection, true, currentModulePosition));
                    }
                }
            }

            MoveToNextTile(ref curX, ref curZ, curDirection);

            if (HasReachedBoundary(curX, curZ))
            {
                FinalizePath(cubes, curX, curZ, curDirection, currentModuleInfo);
                yield break;
            }

            yield return new WaitForSeconds(0.07f);
        }
    }
    private CurrentDirection GetBranchDirection(CurrentDirection curDirection, CurrentDirection origDirection)
    {
        // Find a direction that is different from both curDirection and origDirection
        CurrentDirection[] allDirections = { CurrentDirection.DOWN, CurrentDirection.LEFT, CurrentDirection.RIGHT };
        
        foreach (CurrentDirection dir in allDirections)
        {
            if (dir != curDirection && dir != origDirection)
            {
                return dir;
            }
        }
        
        // Fallback: return opposite of current direction
        if (curDirection == CurrentDirection.LEFT)
            return CurrentDirection.RIGHT;
        else if (curDirection == CurrentDirection.RIGHT)
            return CurrentDirection.LEFT;
        else
            return CurrentDirection.DOWN;
    }
    private bool ThreeUniqueDirections(CurrentDirection curDirection, CurrentDirection origDirection, CurrentDirection branchDirection)
    {
        return curDirection != origDirection &&
               curDirection != branchDirection &&
               origDirection != branchDirection;
    }

    //Open the path setting off the cubes on de chunk grid
    public void ClearTile(GameObject[,] cubes, int x, int z)
    {
        if (cubes[x, z] != null)
        {
            _pool.ReturnObject(cubes[x, z]);
            cubes[x, z] = null;
        }
    }
    
    //Detects the center
    private bool IsAtCenter(int x, int z)
    {
        return z == _mapHeight / 2 && x == _mapWidth / 2;
    }

    //Avoids repetition of last direction 
    private CurrentDirection DetermineNextDirection(int curX, int curZ, CurrentDirection curDirection, HashSet<CurrentDirection> usedDirections, PathGenerationContext context)
    {
        // Use context's own random generator
        int randomValue = context.Random.Next(0, 3);
        CurrentDirection randomDirection = GetDirectionFromRandomValue(randomValue, curX, curZ, curDirection, context);

        if (randomDirection == curDirection)
        {
            if (!context.IsRepeating)
            {
                context.IsRepeating = true;
                usedDirections.Add(randomDirection);
                return randomDirection;
            }
            else
            {
                return DetermineNextDirection(curX, curZ, randomDirection, usedDirections, context);
            }
        }
        else
        {
            context.IsRepeating = false;
            usedDirections.Add(randomDirection);
            return randomDirection;
        }
    }
    //Chooses direction
    private CurrentDirection GetDirectionFromRandomValue(int value, int curX, int curZ, CurrentDirection curDirection, PathGenerationContext context)
    {
        // Add variation based on module seed to ensure different paths
        int variation = (curX + curZ * 7 + context.ModuleSeed) % 3;
        int adjustedValue = (value + variation) % 3;
        
        bool canGoLeft = adjustedValue == 1 && curX > 1 && curDirection != CurrentDirection.RIGHT;
        bool canGoRight = adjustedValue == 2 && curX < _mapWidth - 2 && curDirection != CurrentDirection.LEFT;
        bool canGoDown = adjustedValue == 0 && curZ < _mapHeight - 1;

        if (canGoLeft) return CurrentDirection.LEFT;
        if (canGoRight) return CurrentDirection.RIGHT;
        if (canGoDown) return CurrentDirection.DOWN;

        return curDirection;
    }

    //Cleans the direction history
    private void ResetDirectionHistory(HashSet<CurrentDirection> usedDirections, CurrentDirection curDirection)
    {
        usedDirections.Clear();
        usedDirections.Add(curDirection);
    }
    //moves through the grid
    public void MoveToNextTile(ref int curX, ref int curZ, CurrentDirection curDirection)
    {
        if (curDirection == CurrentDirection.LEFT)
        {
            curX = Mathf.Max(0, curX - 1);
        }
        else if (curDirection == CurrentDirection.RIGHT)
        {
            curX = Mathf.Min(_mapWidth - 1, curX + 1);
        }
        else if (curDirection == CurrentDirection.DOWN)
        {
            curZ++;
        }
    }
    //Detects the end of the module
    public bool HasReachedBoundary(int curX, int curZ)
    {
        return curZ == _mapHeight - 1 || curX == 0 || curX == _mapWidth - 1;
    }
    //Sends the info of the path ending to create a new one
    private void FinalizePath(GameObject[,] cubes, int curX, int curZ, CurrentDirection curDirection, ModuleInfo currentModuleInfo)
    {
        ClearTile(cubes, curX, curZ);

        // Main path: create next module using current module's position as base
        // This ensures the path continues from the correct position
        Vector3 currentModulePosition = currentModuleInfo.NextModulePosition;
        _mapGenerator.DecideNextModulePosition(curX, curZ, curDirection, currentModulePosition);
    }
    
    //Pre-creates module info for branch path to ensure independence
    private void PreCreateBranchModuleInfo(int startX, int startZ, CurrentDirection branchDirection, Vector3 currentModulePosition)
    {
        // Calculate where branch will exit (simulate movement to boundary)
        int branchExitX = startX;
        int branchExitZ = startZ;
        
        // Simulate branch path to find exit point
        while (!HasReachedBoundary(branchExitX, branchExitZ))
        {
            if (branchDirection == CurrentDirection.LEFT)
            {
                branchExitX = Mathf.Max(0, branchExitX - 1);
            }
            else if (branchDirection == CurrentDirection.RIGHT)
            {
                branchExitX = Mathf.Min(_mapWidth - 1, branchExitX + 1);
            }
            else if (branchDirection == CurrentDirection.DOWN)
            {
                branchExitZ++;
            }
            
            if (HasReachedBoundary(branchExitX, branchExitZ))
                break;
        }
        
        // Create module info for branch immediately with its own independent state
        _mapGenerator.DecideNextModulePosition(branchExitX, branchExitZ, branchDirection, currentModulePosition);
    }
    
    //Generates a new branch on the path
    private IEnumerator GenerateBranch(GameObject[,] cubes, int startX, int startZ, CurrentDirection direction, bool isBranch, Vector3 currentModulePosition)
    {
        int curX = startX;
        int curZ = startZ;

        while (!HasReachedBoundary(curX, curZ))
        {
            ClearTile(cubes, curX, curZ);

            MoveToNextTile(ref curX, ref curZ, direction);

            if (HasReachedBoundary(curX, curZ))
            {
                ClearTile(cubes, curX, curZ);
                // Module info was already created in PreCreateBranchModuleInfo
                yield break;
            }

            yield return new WaitForSeconds(0.07f);
        }
    }
    
}
