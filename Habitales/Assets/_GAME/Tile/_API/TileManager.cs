using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Core manager for tile data and grid logic.
/// Handles tile storage, adjacency, entity spawning, and grid queries.
/// For Gavin's procedural shenanigans
/// </summary>
public class TileManager : MonoBehaviour
{
    [Header("Visualization (Optional)")] [SerializeField]
    private GameObject tilePrefab; // 3D tile prefab with TileVisualizer

    [SerializeField] private Transform tileParent; // Parent object for organization
    [SerializeField] private GameObject entityVisualizerPrefab;

    private Dictionary<Vector2Int, Tile> tileCache;
    private Dictionary<Tile, GameObject> tileGameObjects; // Links data to GameObjects

    #region Initialization

    void Awake()
    {
        InitializeGrid();
    }

    /// <summary>
    /// Creates empty grid structure. Call this before spawning tiles.
    /// </summary>
    public void InitializeGrid()
    {
        tileCache = new Dictionary<Vector2Int, Tile>();
        tileGameObjects = new Dictionary<Tile, GameObject>();
    }

    #endregion

    #region Tile Spawning & Creation

    /// <summary>
    /// Spawns a tile at grid position with custom stats.
    /// Returns the created Tile data object.
    /// </summary>
    /// <param name="x">Grid X coordinate</param>
    /// <param name="y">Grid Y coordinate</param>
    /// <param name="stats">Custom TileStats (optional, defaults to random)</param>
    /// <param name="regionID">Zone/region identifier</param>
    public Tile SpawnTile(int x, int y, TileStats stats = null, int regionID = 0)
    {
        Vector2Int pos = new Vector2Int(x, y);

        if (tileCache.ContainsKey(pos))
        {
            Debug.LogWarning($"Tile already exists at ({x}, {y}) — skipping.");
            return tileCache[pos];
        }

        Tile tile = new Tile
        {
            gridPosition = pos,
            stats = stats ?? GenerateDefaultStats(),
            regionID = regionID
        };

        tileCache[tile.gridPosition] = tile;

        if (tilePrefab != null)
            InstantiateTileVisual(tile);

        return tile;
    }

    /// <summary>
    /// Spawns multiple tiles in a rectangular area.
    /// Useful for zone generation.
    /// </summary>
    public List<Tile> SpawnTileArea(int startX, int startY, int width, int height, int regionID = 0)
    {
        List<Tile> spawnedTiles = new List<Tile>();

        for (int x = startX; x < startX + width; x++)
        {
            for (int y = startY; y < startY + height; y++)
            {
                Tile tile = SpawnTile(x, y, null, regionID);
                if (tile != null)
                {
                    spawnedTiles.Add(tile);
                }
            }
        }

        return spawnedTiles;
    }

    /// <summary>
    /// Spawns tiles from a list of grid positions.
    /// Perfect for irregular/organic zone shapes.
    /// </summary>
    public List<Tile> SpawnTilesFromPositions(List<Vector2Int> positions, int regionID = 0)
    {
        List<Tile> spawnedTiles = new List<Tile>();

        foreach (Vector2Int pos in positions)
        {
            Tile tile = SpawnTile(pos.x, pos.y, null, regionID);
            if (tile != null)
            {
                spawnedTiles.Add(tile);
            }
        }

        return spawnedTiles;
    }

    /// <summary>
    /// Generates default random stats for a tile.
    /// Override this or pass custom stats to SpawnTile for procedural variety.
    /// </summary>
    private TileStats GenerateDefaultStats()
    {
        return new TileStats
        {
            nutrientBalance    = Random.Range(10f, 20f),
            soilOrganicMatter  = Random.Range(10f, 20f),
            soilStructure      = Random.Range(10f, 20f),
            biologicalActivity = Random.Range(5f,  15f),
            waterDynamics      = Random.Range(10f, 20f),
            erosionResistance  = Random.Range(5f,  15f),
            vegetationCover    = Random.Range(60f, 70f),
            contamination      = Random.Range(0f,  20f)
        };
    }


    #endregion

    #region Tile Queries & Access

    /// <summary>
    /// Gets tile at grid coordinates. Returns null if out of bounds.
    /// </summary>
    public Tile GetTile(int x, int y)
    {
        tileCache.TryGetValue(new Vector2Int(x, y), out Tile tile);
        return tile; // returns null naturally if not found
    }

    public Tile GetTile(Vector2Int position)
    {
        tileCache.TryGetValue(position, out Tile tile);
        return tile;
    }

    /// <summary>
    /// Gets all tiles in a specific region/zone.
    /// </summary>
    public List<Tile> GetTilesInRegion(int regionID)
    {
        List<Tile> regionTiles = new List<Tile>();

        foreach (Tile tile in tileCache.Values)
        {
            if (tile.regionID == regionID)
            {
                regionTiles.Add(tile);
            }
        }

        return regionTiles;
    }

    /// <summary>
    /// Gets all tiles currently in the grid.
    /// </summary>
    public List<Tile> GetAllTiles()
    {
        return new List<Tile>(tileCache.Values);
    }

    /// <summary>
    /// Gets tiles within a radius of a center position.
    /// </summary>
    public List<Tile> GetTilesInRadius(Vector2Int center, int radius)
    {
        List<Tile> tilesInRadius = new List<Tile>();

        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                Tile tile = GetTile(center.x + x, center.y + y);
                if (tile != null)
                {
                    tilesInRadius.Add(tile);
                }
            }
        }

        return tilesInRadius;
    }

    /// <summary>
    /// Gets 4-directional neighbors (N/S/E/W).
    /// Critical for fire spread and adjacency checks.
    /// </summary>
    public List<Tile> GetAdjacentTiles(Tile tile)
    {
        var neighbors = new List<Tile>();
        Vector2Int[] directions =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        foreach (var dir in directions)
        {
            var neighbor = GetTile(
                tile.gridPosition.x + dir.x,
                tile.gridPosition.y + dir.y
            );
            if (neighbor != null)
            {
                neighbors.Add(neighbor);
            }
        }

        return neighbors;
    }

    /// <summary>
    /// Gets 8-directional neighbors (includes diagonals).
    /// </summary>
    public List<Tile> GetAdjacentTiles8Dir(Tile tile)
    {
        var neighbors = new List<Tile>();

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue; // Skip center

                Tile neighbor = GetTile(
                    tile.gridPosition.x + x,
                    tile.gridPosition.y + y
                );
                if (neighbor != null)
                {
                    neighbors.Add(neighbor);
                }
            }
        }

        return neighbors;
    }

    /// <summary>
    /// Checks if grid position is valid (within bounds).
    /// </summary>
    public bool HasTileAt(int x, int y)
    {
        return tileCache.ContainsKey(new Vector2Int(x, y));
    }
    
    /// <summary>
    /// Gets the GameObject associated with a tile (for visual updates).
    /// </summary>
    public GameObject GetTileGameObject(Tile tile)
    {
        if (tile == null || !tileGameObjects.ContainsKey(tile))
        {
            return null;
        }
        return tileGameObjects[tile];
    }

    #endregion

    #region Tile Modification

    /// <summary>
    /// Applies an IssueConfig to a tile (for zone generation).
    /// </summary>
    public void ApplyIssue(Tile tile, TileIssue issue)
    {
        if (tile == null || issue == null) return;
        tile.stats.nutrientBalance    *= issue.nutrientMult;
        tile.stats.soilOrganicMatter  *= issue.organicMult;
        tile.stats.soilStructure      *= issue.structureMult;
        tile.stats.biologicalActivity *= issue.biologicalMult;
        tile.stats.waterDynamics      *= issue.waterDynMult;
        tile.stats.erosionResistance  *= issue.erosionMult;
        tile.stats.vegetationCover    *= issue.vegetationMult;
        tile.stats.contamination       = Mathf.Min(100f, tile.stats.contamination + issue.contaminationAdd);
        tile.issues.Add(issue);
        if (tile.stats.contamination > 60f && !tile.tv.Contains(TileOverlayType.Contaminated))
            tile.tv.Add(TileOverlayType.Contaminated);
        UpdateTileVisual(tile);
    }


    /// <summary>
    /// Modifies tile stats directly and updates visual.
    /// </summary>
    public void ModifyTileStats(Tile tile, float soilDelta = 0, float vegDelta = 0, float contamDelta = 0)
    {
        if (tile == null) return;
        if (soilDelta != 0)
        {
            float perStat = soilDelta / 6f;
            tile.stats.nutrientBalance    = Mathf.Clamp(tile.stats.nutrientBalance    + perStat, 0f, 100f);
            tile.stats.soilOrganicMatter  = Mathf.Clamp(tile.stats.soilOrganicMatter  + perStat, 0f, 100f);
            tile.stats.soilStructure      = Mathf.Clamp(tile.stats.soilStructure      + perStat, 0f, 100f);
            tile.stats.biologicalActivity = Mathf.Clamp(tile.stats.biologicalActivity + perStat, 0f, 100f);
            tile.stats.waterDynamics      = Mathf.Clamp(tile.stats.waterDynamics      + perStat, 0f, 100f);
            tile.stats.erosionResistance  = Mathf.Clamp(tile.stats.erosionResistance  + perStat, 0f, 100f);
        }
        tile.stats.vegetationCover = Mathf.Clamp(tile.stats.vegetationCover + vegDelta,   0f, 100f);
        tile.stats.contamination   = Mathf.Clamp(tile.stats.contamination   + contamDelta, 0f, 100f);
        UpdateTileVisual(tile);
    }


    #endregion

    #region Entity Management

    /// <summary>
    /// Spawns an entity on a tile and creates its visual representation.
    /// Called during zone generation or dynamic spawning (fires, trash).
    /// </summary>
    public void SpawnEntity<T>(Tile tile) where T : TileEntity, new()
    {
        if (tile == null) return;

        if (tile.entity != null)
        {
            RemoveEntity(tile);
        }
        
        // Create entity data
        tile.entity = new T();

        // Instantiate visual if tile has a GameObject
        if (tileGameObjects.ContainsKey(tile))
        {
            GameObject tileObj = tileGameObjects[tile];
            InstantiateEntityVisual(tile, tileObj);
        }
    }

    /// <summary>
    /// Transforms an existing entity to a new type.
    /// Example: Tree → DeadTree when soil degrades.
    /// </summary>
    public void TransformEntity<T>(Tile tile) where T : TileEntity, new()
    {
        if (tile == null || tile.entity == null) return;

        string oldType = tile.entity.entityType;

        // Replace entity data
        tile.entity = new T();

        // Update visual
        if (tileGameObjects.ContainsKey(tile))
        {
            GameObject tileObj = tileGameObjects[tile];
            EntityVisualizer visualizer = tileObj.GetComponentInChildren<EntityVisualizer>();

            if (visualizer != null)
            {
                visualizer.SetEntity(tile.entity);
                Debug.Log($"Transformed entity at {tile.gridPosition}: {oldType} → {tile.entity.entityType}");
            }
        }
    }

    /// <summary>
    /// Removes entity from a tile and destroys its visual.
    /// </summary>
    public void RemoveEntity(Tile tile)
    {
        if (tile == null || tile.entity == null) return;

        // Destroy visual
        if (tileGameObjects.ContainsKey(tile))
        {
            GameObject tileObj = tileGameObjects[tile];
            EntityVisualizer[] visualizers = tileObj.GetComponentsInChildren<EntityVisualizer>();
            foreach (EntityVisualizer v in visualizers)
            {
                if (v != null)
                    Destroy(v.gameObject);
            }
        }

        tile.entity = null;
    }

    /// <summary>
    /// Creates the visual GameObject for an entity.
    /// </summary>
    private void InstantiateEntityVisual(Tile tile, GameObject parentTileObj) {
        if (tile.entity == null) return;
    
        // Instantiate from prefab instead of creating new GameObject
        GameObject entityObj = Instantiate(entityVisualizerPrefab, parentTileObj.transform);
        entityObj.name = $"Entity_{tile.entity.entityType}";
        entityObj.transform.localPosition = Vector3.zero;
    
        // Get existing visualizer component
        EntityVisualizer visualizer = entityObj.GetComponent<EntityVisualizer>();
        visualizer.Initialize(tile.entity, tile);
    }


    /// <summary>
    /// Calls OnDailyUpdate for all entities in a region.
    /// Should be called once per day during cascade.
    /// </summary>
    public void UpdateEntitiesInRegion(int regionID)
    {
        List<Tile> regionTiles = GetTilesInRegion(regionID);

        foreach (Tile tile in regionTiles)
        {
            if (tile.entity != null)
            {
                tile.entity.OnDailyUpdate(tile, this);
            }
        }
    }
    
    /// <summary>
    /// Calls OnDailyUpdate for ALL entities in the entire game world.
    /// Use this for global daily updates (fires, villages, factories, etc.)
    /// </summary>
    public void UpdateAllEntities()
    {
        List<Tile> allTiles = GetAllTiles();
        foreach (Tile tile in allTiles)
        {
            if (tile.entity != null)
            {
                tile.entity.OnDailyUpdate(tile, this);
            }
        }
    }

    #endregion

    #region Visualization

    /// <summary>
    /// Creates the 3D GameObject for a tile.
    /// Called automatically by SpawnTile if tilePrefab is set.
    /// </summary>
    private void InstantiateTileVisual(Tile tile)
    {
        Vector3 worldPos = GridToWorldPosition(tile.gridPosition);

        GameObject tileObj = Instantiate(tilePrefab, worldPos, Quaternion.identity, tileParent);
        tileObj.name = $"Tile_{tile.gridPosition.x}_{tile.gridPosition.y}";

        // Initialize visualizer
        TileVisualizer visualizer = tileObj.GetComponent<TileVisualizer>();
        if (visualizer != null)
        {
            visualizer.Initialize(tile);
        }

        tileGameObjects[tile] = tileObj;
    }

    /// <summary>
    /// Updates tile visual after stat changes.
    /// </summary>
    public void UpdateTileVisual(Tile tile)
    {
        if (tile == null || !tileGameObjects.ContainsKey(tile)) return;
        GameObject tileObj = tileGameObjects[tile];
        TileVisualizer visualizer = tileObj.GetComponent<TileVisualizer>();
        if (visualizer == null) return;
        visualizer.UpdateVisuals();
        visualizer.UpdateOverlays(tile.tv);
    }

    /// <summary>
    /// Converts grid coordinates to world position.
    /// Adjust this based on your 3D tilemap setup.
    /// </summary>
    public Vector3 GridToWorldPosition(Vector2Int gridPos)
    {
        // Based on your description: {0.5, 0.5} offset from origin
        // Adjust Y position as needed (0 for flat ground, or custom height)
        return new Vector3(
            gridPos.x + 0.5f,
            0f, // Ground level - adjust if tiles have height variation
            gridPos.y + 0.5f
        );
    }

    #endregion

    #region Properties
    
    public Vector2Int WorldMin
    {
        get
        {
            int minX = int.MaxValue, minY = int.MaxValue;
            foreach (var pos in tileCache.Keys)
            {
                if (pos.x < minX) minX = pos.x;
                if (pos.y < minY) minY = pos.y;
            }
            return tileCache.Count == 0 ? Vector2Int.zero : new Vector2Int(minX, minY);
        }
    }

    public Vector2Int WorldMax
    {
        get
        {
            int maxX = int.MinValue, maxY = int.MinValue;
            foreach (var pos in tileCache.Keys)
            {
                if (pos.x > maxX) maxX = pos.x;
                if (pos.y > maxY) maxY = pos.y;
            }
            return tileCache.Count == 0 ? Vector2Int.zero : new Vector2Int(maxX, maxY);
        }
    }


    #endregion
}