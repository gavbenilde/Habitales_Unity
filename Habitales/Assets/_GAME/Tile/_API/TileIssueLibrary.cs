using UnityEngine;

public static class TileIssueLibrary
{
    public static TileIssue LoggedTrees => new TileIssue
    {
        type           = IssueType.LoggedTrees,
        vegetationMult = 0.15f,
        organicMult    = 0.60f,
        structureMult  = 0.70f
    };

    public static TileIssue NutrientDepletion => new TileIssue
    {
        type         = IssueType.NutrientDepletion,
        nutrientMult = 0.25f,
        organicMult  = 0.40f
    };

    public static TileIssue HeavyMetalContamination => new TileIssue
    {
        type             = IssueType.HeavyMetalContamination,
        biologicalMult   = 0.30f,
        contaminationAdd = 65f
    };
    public static TileIssue ActiveErosion = new TileIssue
    {
        type = IssueType.ActiveErosion,
        erosionMult = 0.20f,
        structureMult = 0.60f,
        waterDynMult = 0.70f
    };

    public static TileIssue DrainageCollapse = new TileIssue
    {
        type = IssueType.DrainageCollapse,
        waterDynMult = 0.20f,
        structureMult = 0.55f,
        biologicalMult = 0.70f
    };

    public static TileIssue SoilCompaction = new TileIssue
    {
        type = IssueType.SoilCompaction,
        structureMult = 0.15f,
        waterDynMult = 0.60f,
        biologicalMult = 0.65f
    };

    public static TileIssue ChemicalBurnout = new TileIssue
    {
        type = IssueType.ChemicalBurnout,
        biologicalMult = 0.20f,
        nutrientMult = 0.45f,
        organicMult = 0.55f,
        contaminationAdd = 30f
    };

    // Lookup by IssueType — used by ZoneManager during issue assignment
    public static TileIssue Get(IssueType type)
    {
        switch (type)
        {
            case IssueType.LoggedTrees:             return LoggedTrees;
            case IssueType.NutrientDepletion:       return NutrientDepletion;
            case IssueType.HeavyMetalContamination: return HeavyMetalContamination;
            case IssueType.ActiveErosion:           return ActiveErosion;
            case IssueType.DrainageCollapse:        return DrainageCollapse;
            case IssueType.SoilCompaction:          return SoilCompaction;
            case IssueType.ChemicalBurnout:         return ChemicalBurnout;
            default:
                Debug.LogWarning($"TileIssueLibrary: No entry for IssueType {type}");
                return null;
        }
    }
}