// using System.Collections.Generic;
// using UnityEngine;
//
// public class HardCode : MonoBehaviour
// {
//     [SerializeField] private TileManager tileManager;
//     [SerializeField] private ZoneManager zoneManager;
//     [SerializeField] private GameManager gameManager;
//     [SerializeField] private ActionManager actionManager;
//     [SerializeField] private ResourceManager resourceManager;
//
//     private int rng;
//     private bool findSecondZone;
//     private bool findThirdZone;
//
//     #region Entity Spawning In Zone Context
//
//     public void SpawnRandomEntityInFirstZone()
//     {
//         List<Tile> zoneTiles = tileManager.GetTilesInRegion(1);
//
//         foreach (Tile tile in zoneTiles)
//         {
//             
//             rng = Randomize();
//
//             if (rng <= 8)
//                 tileManager.SpawnEntity<TreeEntity>(tile);
//             else if (rng >= 85)
//                 tileManager.SpawnEntity<StumpEntity>(tile);
//         }
//     }
//
//     public void SpawnRandomEntityInSecondZone()
//     {
//         List<Tile> zoneTiles = tileManager.GetTilesInRegion(2);
//
//         int villageCap = 0;
//         
//         foreach (Tile tile in zoneTiles)
//         {
//             rng = Randomize();
//             
//             if (rng >= 40 && rng <= 50 && villageCap < 1)
//             {
//                 tileManager.SpawnEntity<VillageEntity>(tile);
//                 villageCap++;
//             }
//             else if (rng < 10)
//                 tileManager.SpawnEntity<TreeEntity>(tile);
//         }
//     }
//
//     public void SpawnRandomEntityInThirdZone()
//     {
//         List<Tile> zoneTiles = tileManager.GetTilesInRegion(3);
//
//         int villageCap = 0;
//
//         foreach (Tile tile in zoneTiles)
//         {
//             rng = Randomize();
//
//             if (rng >= 40 && rng <= 50 && villageCap < 2)
//             {
//                 tileManager.SpawnEntity<VillageEntity>(tile);
//                 villageCap++;
//             }
//             else if (rng >= 60 && rng <= 80)
//                 tileManager.SpawnEntity<StumpEntity>(tile);
//             else if (rng >= 80 && rng <= 90)
//                 tileManager.SpawnEntity<TreeEntity>(tile);
//         }
//     }
//
//     #endregion
//
//     #region Zone Generating
//
//     public void GenerateSecondZone()
//     {
//         if (tileManager == null)
//         {
//             Debug.LogError("TileManager reference missing! Assign it in Inspector.");
//             return;
//         }
//         if (findSecondZone)
//         {
//             Debug.LogError("Second zone has already spawned!");
//             return;
//         }
//
//         Debug.Log($"Generating {tileManager.GridWidth}x{tileManager.GridHeight} grid...");
//         int tilesSpawned = 0;
//
//         for (int x = 6; x < 12; x++)
//         {
//             for (int y = 2; y < 8; y++)
//             {
//                 Tile tile = tileManager.SpawnTile(x, y, null, 2);
//                 if (tile != null) tilesSpawned++;
//             }
//         }
//
//         resourceManager.increaseTotalPeople(4);
//         findSecondZone = true;
//
//         Debug.Log($"? Successfully spawned {tilesSpawned} tiles!");
//
//         // ============================================================================================
//
//         List<Tile> zoneTiles = tileManager.GetTilesInRegion(2);
//
//         foreach (Tile tile in zoneTiles)
//         {
//             tile.stats.soilQuality = Random.Range(15f, 30f);
//             tile.stats.vegetationCover = Random.Range(5f, 30f);
//             tile.stats.contamination = Random.Range(0f, 10f);
//             tile.stats.waterPurity = 100f;
//             tile.stats.hasFirebreak = false;
//
//             // Initialize issues list
//             tile.issues = new List<IssueType>();
//
//             tileManager.UpdateTileVisual(tile);
//         }
//
//         SpawnRandomEntityInSecondZone();
//     }
//
//     public void GenerateThirdZone()
//     {
//         if (tileManager == null)
//         {
//             Debug.LogError("TileManager reference missing! Assign it in Inspector.");
//             return;
//         }
//         if (findThirdZone)
//         {
//             Debug.LogError("Third zone has already spawned!");
//             return;
//         }
//
//         Debug.Log($"Generating {tileManager.GridWidth}x{tileManager.GridHeight} grid...");
//         int tilesSpawned = 0;
//
//         for (int x = 4; x < 10; x++)
//         {
//             for (int y = 8; y < 14; y++)
//             {
//                 Tile tile = tileManager.SpawnTile(x, y, null, 3);
//                 if (tile != null) tilesSpawned++;
//             }
//         }
//
//         resourceManager.increaseTotalPeople(4);
//         findThirdZone = true;
//
//         Debug.Log($"? Successfully spawned {tilesSpawned} tiles!");
//
//         // ============================================================================================
//
//         List<Tile> zoneTiles = tileManager.GetTilesInRegion(3);
//
//         foreach (Tile tile in zoneTiles)
//         {
//             tile.stats.soilQuality = Random.Range(10f, 25f);
//             tile.stats.vegetationCover = Random.Range(5f, 25f);
//             tile.stats.contamination = Random.Range(0f, 10f);
//             tile.stats.waterPurity = 100f;
//             tile.stats.hasFirebreak = false;
//
//             // Initialize issues list
//             tile.issues = new List<IssueType>();
//
//             tileManager.UpdateTileVisual(tile);
//         }
//
//         SpawnRandomEntityInThirdZone();
//     }
//
//     #endregion
//
//     public int Randomize()
//     {
//         int _rng = Random.Range(0, 100);
//
//         return _rng;
//     }
// }