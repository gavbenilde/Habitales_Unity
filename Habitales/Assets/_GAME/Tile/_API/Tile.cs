using System.Collections.Generic;
using UnityEngine;

public class Tile
{
    public Vector2Int gridPosition;
    public TileStats stats;
    public TileEntity entity;
    public List<TileIssue> issues = new List<TileIssue>();
    public List<TileOverlayType> tv = new List<TileOverlayType>();
    public int regionID;

    public float CalculateHealth() => stats.CalculateHealth();
    public float GetSoilComposite() => stats.soilComposite;
    public float GetVegetationCover() => stats.vegetationCover;
    public float GetContamination() => stats.contamination;
    
    public bool isAnalyzed = false;
    public bool issuesRevealed = false;


}

[SerializeField]
public class TileStats
{
    public float nutrientBalance    = 50f;
    public float soilOrganicMatter  = 50f;
    public float soilStructure      = 50f;
    public float biologicalActivity = 50f;
    public float waterDynamics      = 50f;
    public float erosionResistance  = 50f;
    public float vegetationCover    = 50f;
    public float contamination      = 0f;

    public float soilComposite =>
        (nutrientBalance + soilOrganicMatter + soilStructure +
         biologicalActivity + waterDynamics + erosionResistance) / 6f;

    public float CalculateHealth()
    {
        float baseHealth = (soilComposite + vegetationCover + (100f - contamination)) / 3f;
        return contamination > 60f ? Mathf.Min(baseHealth, 33f) : baseHealth;
    }
}
