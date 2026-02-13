using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Tile {
    public TileStats stats;
    public TileEntity entity;
    public List<IssueType> issues;
    public Vector2Int gridPosition;
    public int regionID;
    
    public bool issuesRevealed = false;
    
    public float CalculateHealth() => stats.CalculateHealth();
}

[SerializeField]
public class TileStats {
    public float soilQuality;
    public float vegetationCover;
    public float contamination;
    public float waterPurity = 100f;
    public bool hasFirebreak;

    public static readonly float SOIL_QUALITY_MAX = 100f;
    public static readonly float VEGETATION_COVER_MAX = 100f;
    public static readonly float CONTAMINATION_MAX = 100f;

    
    
    public float CalculateHealth() {
        return (0.4f * soilQuality) + // 40%
               (0.4f * vegetationCover) + // 40%
               (0.2f * (CONTAMINATION_MAX - contamination)); // 20%
    }
}
