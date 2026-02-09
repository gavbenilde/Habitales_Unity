using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum IssueType {
    LoggedTrees,
    NutrientDepletion,
    HeavyMetalContamination
}

[CreateAssetMenu(fileName = "IssueConfig", menuName = "Habitales/IssueConfig")]
public class IssueConfig : ScriptableObject {
    public IssueType type;
    public float soilQualityModifier;
    public float vegetationModifier;
    public float contaminationModifier;
    
    public void ApplyToTile(Tile tile) {
        tile.stats.soilQuality += soilQualityModifier;
        tile.stats.vegetationCover += vegetationModifier;
        tile.stats.contamination += contaminationModifier;
    }
}