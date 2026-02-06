using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generador de Diagramas de Voronoi para crear zonas orgánicas tipo cueva
/// </summary>
public class VoronoiGenerator
{
    public enum VoronoiCaveShape
    {
        /// <summary>Classic Voronoi blobs using euclidean distance (roughly circular).</summary>
        Circle = 0,
        /// <summary>Voronoi blobs using Chebyshev distance (square-ish).</summary>
        Square = 1,
        /// <summary>Plus/cross shapes around each seed (bounded by threshold).</summary>
        Cross = 2
    }

    private System.Random _random;
    private int _mapWidth;
    private int _mapHeight;
    
    public VoronoiGenerator(int mapWidth, int mapHeight, int seed)
    {
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        _random = new System.Random(seed);
    }
    
    /// <summary>
    /// Genera puntos de semilla aleatorios para el diagrama de Voronoi
    /// </summary>
    /// <param name="numSeeds">Número de semillas a generar</param>
    /// <param name="margin">Margen desde los bordes para evitar semillas en los límites</param>
    /// <returns>Lista de posiciones de semillas</returns>
    public List<Vector2> GenerateSeeds(int numSeeds, int margin = 1)
    {
        List<Vector2> seeds = new List<Vector2>();
        
        for (int i = 0; i < numSeeds; i++)
        {
            float x = _random.Next(margin, _mapWidth - margin);
            float z = _random.Next(margin, _mapHeight - margin);
            seeds.Add(new Vector2(x, z));
        }
        
        return seeds;
    }
    
    /// <summary>
    /// Calcula a qué semilla pertenece un tile dado (semilla más cercana)
    /// </summary>
    /// <param name="tileX">Coordenada X del tile</param>
    /// <param name="tileZ">Coordenada Z del tile</param>
    /// <param name="seeds">Lista de semillas</param>
    /// <returns>Índice de la semilla más cercana</returns>
    public int GetClosestSeedIndex(int tileX, int tileZ, List<Vector2> seeds)
    {
        float minDistance = float.MaxValue;
        int closestIndex = 0;
        
        Vector2 tilePos = new Vector2(tileX, tileZ);
        
        for (int i = 0; i < seeds.Count; i++)
        {
            float distance = Vector2.Distance(tilePos, seeds[i]);
            if (distance < minDistance)
            {
                minDistance = distance;
                closestIndex = i;
            }
        }
        
        return closestIndex;
    }
    
    /// <summary>
    /// Calcula la distancia a la semilla más cercana
    /// </summary>
    public float GetDistanceToClosestSeed(int tileX, int tileZ, List<Vector2> seeds)
    {
        float minDistance = float.MaxValue;
        Vector2 tilePos = new Vector2(tileX, tileZ);
        
        foreach (Vector2 seed in seeds)
        {
            float distance = Vector2.Distance(tilePos, seed);
            if (distance < minDistance)
            {
                minDistance = distance;
            }
        }
        
        return minDistance;
    }
    
    /// <summary>
    /// Genera una máscara booleana indicando qué tiles deben ser "cueva" (true) o "pared" (false)
    /// </summary>
    /// <param name="numSeeds">Número de semillas para generar</param>
    /// <param name="caveThreshold">Distancia máxima desde una semilla para ser considerado cueva</param>
    /// <param name="preservePaths">Si es true, preserva los tiles que están en los caminos</param>
    /// <param name="pathTiles">HashSet de tiles que son parte de caminos (para preservar)</param>
    /// <returns>Array 2D donde true = cueva (eliminar), false = pared (mantener)</returns>
    public bool[,] GenerateCaveMask(int numSeeds, float caveThreshold, bool preservePaths = true, HashSet<Vector2Int> pathTiles = null)
    {
        bool[,] caveMask = new bool[_mapWidth, _mapHeight];
        List<Vector2> seeds = GenerateSeeds(numSeeds);
        
        // Inicializar todo como pared (false)
        for (int x = 0; x < _mapWidth; x++)
        {
            for (int z = 0; z < _mapHeight; z++)
            {
                caveMask[x, z] = false;
            }
        }
        
        // Marcar zonas de cueva basadas en distancia a semillas
        for (int x = 0; x < _mapWidth; x++)
        {
            for (int z = 0; z < _mapHeight; z++)
            {
                Vector2Int tilePos = new Vector2Int(x, z);
                
                // Si preservePaths está activado y este tile es parte de un camino, mantenerlo
                if (preservePaths && pathTiles != null && pathTiles.Contains(tilePos))
                {
                    caveMask[x, z] = false; // Mantener como pared
                    continue;
                }
                
                // Calcular distancia a la semilla más cercana
                float distance = GetDistanceToClosestSeed(x, z, seeds);
                
                // Si está dentro del umbral, es cueva
                if (distance <= caveThreshold)
                {
                    caveMask[x, z] = true; // Marcar como cueva (eliminar)
                }
            }
        }
        
        return caveMask;
    }
    
    /// <summary>
    /// Genera una máscara usando un enfoque más orgánico con variación de tamaño por región
    /// </summary>
    public bool[,] GenerateOrganicCaveMask(int numSeeds, float baseThreshold, float thresholdVariation, bool preservePaths = true, HashSet<Vector2Int> pathTiles = null)
    {
        // Backward compatible: organic mask = euclidean blobs (circle-like).
        return GenerateVoronoiCaveMask(numSeeds, baseThreshold, thresholdVariation, VoronoiCaveShape.Circle, 0.25f, preservePaths, pathTiles);
    }

    /// <summary>
    /// Voronoi cave mask with selectable shape (circle/square/cross) and per-seed threshold variation.
    /// </summary>
    public bool[,] GenerateVoronoiCaveMask(
        int numSeeds,
        float baseThreshold,
        float thresholdVariation,
        VoronoiCaveShape shape,
        float crossArmWidthFactor = 0.25f,
        bool preservePaths = true,
        HashSet<Vector2Int> pathTiles = null)
    {
        bool[,] caveMask = new bool[_mapWidth, _mapHeight];
        List<Vector2> seeds = GenerateSeeds(numSeeds);
        List<float> seedThresholds = new List<float>(seeds.Count);
        
        // Per-seed threshold variation.
        for (int i = 0; i < seeds.Count; i++)
        {
            float variation = (float)(_random.NextDouble() * 2.0 - 1.0) * thresholdVariation; // -variation .. +variation
            float threshold = baseThreshold + variation;
            threshold = Mathf.Max(threshold, baseThreshold * 0.7f);
            seedThresholds.Add(threshold);
        }
        
        // Init walls.
        for (int x = 0; x < _mapWidth; x++)
        {
            for (int z = 0; z < _mapHeight; z++)
            {
                caveMask[x, z] = false;
            }
        }
        
        // Mark caves.
        for (int x = 0; x < _mapWidth; x++)
        {
            for (int z = 0; z < _mapHeight; z++)
            {
                Vector2Int tilePos = new Vector2Int(x, z);
                
                if (preservePaths && pathTiles != null && pathTiles.Contains(tilePos))
                {
                    caveMask[x, z] = false;
                    continue;
                }
                
                float bestDist = float.PositiveInfinity;
                float bestThreshold = baseThreshold;

                for (int i = 0; i < seeds.Count; i++)
                {
                    float threshold = seedThresholds[i];
                    float d = DistanceToSeedByShape(x, z, seeds[i], shape, threshold, crossArmWidthFactor);
                    if (d < bestDist)
                {
                        bestDist = d;
                        bestThreshold = threshold;
                    }
                }

                if (bestDist <= bestThreshold)
                {
                    caveMask[x, z] = true;
                }
            }
        }
        
        return caveMask;
    }

    private static float DistanceToSeedByShape(int x, int z, Vector2 seed, VoronoiCaveShape shape, float threshold, float crossArmWidthFactor)
    {
        float dx = Mathf.Abs(x - seed.x);
        float dz = Mathf.Abs(z - seed.y);

        switch (shape)
        {
            case VoronoiCaveShape.Square:
                // Chebyshev distance => square-ish blobs.
                return Mathf.Max(dx, dz);
            case VoronoiCaveShape.Cross:
                // Bounded plus sign: two arms crossing at the seed.
                // Arm width is a fraction of the threshold (clamped).
                float arm = Mathf.Clamp(threshold * Mathf.Max(0f, crossArmWidthFactor), 0.5f, Mathf.Max(0.5f, threshold));
                if (dx <= arm) return dz;
                if (dz <= arm) return dx;
                return float.PositiveInfinity;
            case VoronoiCaveShape.Circle:
            default:
                // Euclidean distance => circular blobs.
                return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
    
    /// <summary>
    /// Genera círculos simétricos que se expanden desde el centro del módulo
    /// El centro está vacío y solo los bordes tienen paredes
    /// </summary>
    public bool[,] GenerateCircularCaves(int numCircles, float minRadius, float maxRadius, Vector2Int centerPoint, bool preservePaths = true, HashSet<Vector2Int> pathTiles = null)
    {
        bool[,] caveMask = new bool[_mapWidth, _mapHeight];
        
        // Inicializar todo como pared (false)
        for (int x = 0; x < _mapWidth; x++)
        {
            for (int z = 0; z < _mapHeight; z++)
            {
                caveMask[x, z] = false;
            }
        }
        
        // Calcular el centro del módulo si no se proporciona
        Vector2Int center = centerPoint;
        if (centerPoint.x < 0 || centerPoint.y < 0)
        {
            center = new Vector2Int(_mapWidth / 2, _mapHeight / 2);
        }
        
        // Generar círculos concéntricos
        float radiusStep = (maxRadius - minRadius) / numCircles;
        
        for (int circleIndex = 0; circleIndex < numCircles; circleIndex++)
        {
            float currentRadius = minRadius + (radiusStep * circleIndex);
            float nextRadius = minRadius + (radiusStep * (circleIndex + 1));
            
            // Crear un anillo (ring) entre currentRadius y nextRadius
            for (int x = 0; x < _mapWidth; x++)
            {
                for (int z = 0; z < _mapHeight; z++)
                {
                    Vector2Int tilePos = new Vector2Int(x, z);
                    
                    // Preservar caminos si está activado
                    if (preservePaths && pathTiles != null && pathTiles.Contains(tilePos))
                    {
                        caveMask[x, z] = false;
                        continue;
                    }
                    
                    // Calcular distancia desde el centro
                    float distance = Vector2.Distance(new Vector2(x, z), new Vector2(center.x, center.y));
                    
                    // Si está dentro del radio mínimo, mantener como pared (centro sólido)
                    if (distance < minRadius)
                    {
                        caveMask[x, z] = false; // Pared en el centro
                    }
                    // Si está en el anillo entre minRadius y maxRadius, crear cueva
                    else if (distance >= minRadius && distance <= maxRadius)
                    {
                        caveMask[x, z] = true; // Cueva (eliminar)
                    }
                    // Si está más allá del radio máximo, mantener como pared (bordes)
                    else
                    {
                        caveMask[x, z] = false; // Pared en los bordes
                    }
                }
            }
        }
        
        return caveMask;
    }
    
    /// <summary>
    /// Genera círculos uniformes y simétricos que se expanden desde el centro
    /// El centro está vacío (sin bloques) y solo los bordes tienen paredes
    /// </summary>
    public bool[,] GenerateUniformCircularCaves(float innerRadius, float outerRadius, Vector2Int centerPoint, bool preservePaths = true, HashSet<Vector2Int> pathTiles = null)
    {
        bool[,] caveMask = new bool[_mapWidth, _mapHeight];
        
        // Inicializar todo como pared (false = mantener, true = eliminar)
        for (int x = 0; x < _mapWidth; x++)
        {
            for (int z = 0; z < _mapHeight; z++)
            {
                caveMask[x, z] = false; // Por defecto, mantener (pared)
            }
        }
        
        // Calcular el centro del módulo si no se proporciona
        Vector2Int center = centerPoint;
        if (centerPoint.x < 0 || centerPoint.y < 0)
        {
            center = new Vector2Int(_mapWidth / 2, _mapHeight / 2);
        }
        
        // Generar círculo uniforme y simétrico
        for (int x = 0; x < _mapWidth; x++)
        {
            for (int z = 0; z < _mapHeight; z++)
            {
                Vector2Int tilePos = new Vector2Int(x, z);
                
                // Preservar caminos si está activado (siempre mantener los caminos)
                if (preservePaths && pathTiles != null && pathTiles.Contains(tilePos))
                {
                    caveMask[x, z] = false; // Mantener el camino
                    continue;
                }
                
                // Calcular distancia desde el centro usando distancia euclidiana
                float dx = x - center.x;
                float dz = z - center.y;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                
                // Lógica: 
                // - Centro (distance < innerRadius): VACÍO (eliminar bloques) = true
                // - Anillo medio (innerRadius <= distance <= outerRadius): CUEVA (eliminar) = true
                // - Bordes externos (distance > outerRadius): PARED (mantener) = false
                
                if (distance < innerRadius)
                {
                    // Centro vacío - eliminar bloques
                    caveMask[x, z] = true;
                }
                else if (distance >= innerRadius && distance <= outerRadius)
                {
                    // Anillo de cueva - eliminar bloques
                    caveMask[x, z] = true;
                }
                else
                {
                    // Bordes externos - mantener paredes
                    caveMask[x, z] = false;
                }
            }
        }
        
        return caveMask;
    }
    
    /// <summary>
    /// Genera cuevas en forma de rombo que se expanden desde el centro
    /// El centro está vacío y solo los bordes tienen paredes
    /// </summary>
    public bool[,] GenerateDiamondCaves(float innerSize, float outerSize, Vector2Int centerPoint, bool preservePaths = true, HashSet<Vector2Int> pathTiles = null)
    {
        bool[,] caveMask = new bool[_mapWidth, _mapHeight];
        
        // Inicializar todo como pared (false = mantener, true = eliminar)
        for (int x = 0; x < _mapWidth; x++)
        {
            for (int z = 0; z < _mapHeight; z++)
            {
                caveMask[x, z] = false;
            }
        }
        
        // Calcular el centro del módulo si no se proporciona
        Vector2Int center = centerPoint;
        if (centerPoint.x < 0 || centerPoint.y < 0)
        {
            center = new Vector2Int(_mapWidth / 2, _mapHeight / 2);
        }
        
        // Generar rombo uniforme y simétrico usando distancia Manhattan
        for (int x = 0; x < _mapWidth; x++)
        {
            for (int z = 0; z < _mapHeight; z++)
            {
                Vector2Int tilePos = new Vector2Int(x, z);
                
                // Preservar caminos si está activado
                if (preservePaths && pathTiles != null && pathTiles.Contains(tilePos))
                {
                    caveMask[x, z] = false;
                    continue;
                }
                
                // Calcular distancia Manhattan (dx + dz) para crear forma de rombo
                float dx = Mathf.Abs(x - center.x);
                float dz = Mathf.Abs(z - center.y);
                float distance = dx + dz; // Distancia Manhattan para rombos perfectos
                
                // Lógica similar a círculos pero con forma de rombo
                if (distance < innerSize)
                {
                    // Centro vacío - eliminar bloques
                    caveMask[x, z] = true;
                }
                else if (distance >= innerSize && distance <= outerSize)
                {
                    // Anillo de cueva - eliminar bloques
                    caveMask[x, z] = true;
                }
                else
                {
                    // Bordes externos - mantener paredes
                    caveMask[x, z] = false;
                }
            }
        }
        
        return caveMask;
    }
    
    /// <summary>
    /// Genera cuevas cuadradas que se expanden desde el centro
    /// El centro está vacío y solo los bordes tienen paredes
    /// </summary>
    public bool[,] GenerateSquareCaves(float innerSize, float outerSize, Vector2Int centerPoint, bool preservePaths = true, HashSet<Vector2Int> pathTiles = null)
    {
        bool[,] caveMask = new bool[_mapWidth, _mapHeight];
        
        // Inicializar todo como pared (false = mantener, true = eliminar)
        for (int x = 0; x < _mapWidth; x++)
        {
            for (int z = 0; z < _mapHeight; z++)
            {
                caveMask[x, z] = false;
            }
        }
        
        // Calcular el centro del módulo si no se proporciona
        Vector2Int center = centerPoint;
        if (centerPoint.x < 0 || centerPoint.y < 0)
        {
            center = new Vector2Int(_mapWidth / 2, _mapHeight / 2);
        }
        
        // Generar cuadrado uniforme y simétrico usando distancia Chebyshev
        for (int x = 0; x < _mapWidth; x++)
        {
            for (int z = 0; z < _mapHeight; z++)
            {
                Vector2Int tilePos = new Vector2Int(x, z);
                
                // Preservar caminos si está activado
                if (preservePaths && pathTiles != null && pathTiles.Contains(tilePos))
                {
                    caveMask[x, z] = false;
                    continue;
                }
                
                // Calcular distancia Chebyshev (Max(dx, dz)) para crear forma cuadrada perfecta
                float dx = Mathf.Abs(x - center.x);
                float dz = Mathf.Abs(z - center.y);
                float distance = Mathf.Max(dx, dz); // Distancia Chebyshev para cuadrados perfectos
                
                // Lógica similar a círculos pero con forma cuadrada
                if (distance < innerSize)
                {
                    // Centro vacío - eliminar bloques
                    caveMask[x, z] = true;
                }
                else if (distance >= innerSize && distance <= outerSize)
                {
                    // Anillo de cueva - eliminar bloques
                    caveMask[x, z] = true;
                }
                else
                {
                    // Bordes externos - mantener paredes
                    caveMask[x, z] = false;
                }
            }
        }
        
        return caveMask;
    }
}
