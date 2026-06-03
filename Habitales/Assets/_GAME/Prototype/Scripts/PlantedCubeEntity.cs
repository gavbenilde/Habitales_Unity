using UnityEngine;

public class PlantedCubeEntity : TileEntity
{
    // Scales daily substat contributions. Bumped from 1f to 2f so plants
    // improve tile substats ~2x faster (prototype tuning knob).
    private const float plantContributionMultiplier = 2f;
    private const float baseContributionRate = 0.05f;

    // A living plant is literal vegetation — it raises the tile's vegetation cover each day.
    private const float vegetationContributionPerDay = 5f;

    public PlantingProfileSO profile;
    public int currentTier = 0;
    public int daysAtCurrentTier = 0;
    public bool isWithered = false;

    // Set by PlantingAction after spawn; drives visual updates.
    public Transform cubeTransform;

    public PlantedCubeEntity() { entityType = "PlantedCube"; }

    public override void OnDailyUpdate(Tile tile, TileManager manager)
    {
        if (isWithered) return;

        bool surviving = true;
        bool thriving = true;

        for (int i = 0; i < profile.survivalArray.Length; i++)
        {
            float threshold = profile.survivalArray[i];
            if (threshold <= 0f) continue;

            float statVal = GetStat(tile.stats, i);
            if (statVal < threshold * 0.5f) { surviving = false; thriving = false; break; }
            if (statVal < threshold) thriving = false;
        }

        if (!surviving)
        {
            isWithered = true;
            RefreshVisual(tile);
            Habitales.Dialogue.DialogueManager.Instance?.AppendAziMessage(
                $"The plant died. They could only survive extreme {profile.SpecialtyStatName}.");
            return;
        }

        // Contribute to substats
        for (int i = 0; i < profile.survivalArray.Length; i++)
        {
            if (profile.survivalArray[i] <= 0f) continue;
            SetStat(tile.stats, i,
                Mathf.Clamp(GetStat(tile.stats, i) + profile.survivalArray[i] * baseContributionRate * plantContributionMultiplier, 0f, 100f));
        }

        // Vegetation cover isn't in the survival array — bump it directly.
        tile.stats.vegetationCover =
            Mathf.Clamp(tile.stats.vegetationCover + vegetationContributionPerDay, 0f, 100f);

        if (thriving)
        {
            daysAtCurrentTier++;
            if (daysAtCurrentTier >= profile.daysPerTier && currentTier < profile.tierCount - 1)
            {
                currentTier++;
                daysAtCurrentTier = 0;
            }
        }

        RefreshVisual(tile);
    }

    public void RefreshVisual(Tile tile)
    {
        if (cubeTransform == null) return;

        // Scale: lerp evenly from tier 0 up to tierCount-1 (final = 1.0)
        float scale = (float)(currentTier + 1) / profile.tierCount;
        cubeTransform.localScale = Vector3.one * scale;

        // Whiteness lerps with TILE health — saturated when tile thrives, washed-out when degraded.
        float healthFactor = isWithered ? 0f : Mathf.Clamp01(tile.CalculateHealth() / 100f);
        float whiteness = isWithered ? 0.9f : Mathf.Lerp(1f, 0f, healthFactor);
        Color c = HWBColor.HWBToRGB(profile.hue, whiteness, profile.blackness);

        MeshRenderer mr = cubeTransform.GetComponent<MeshRenderer>();
        if (mr != null) mr.material.color = c;
    }

    // Maps survivalArray index to the matching TileStats field.
    private static float GetStat(TileStats s, int i)
    {
        switch (i)
        {
            case 0: return s.nutrientBalance;
            case 1: return s.soilOrganicMatter;
            case 2: return s.soilStructure;
            case 3: return s.biologicalActivity;
            case 4: return s.waterDynamics;
            case 5: return s.erosionResistance;
            default: return 0f;
        }
    }

    private static void SetStat(TileStats s, int i, float v)
    {
        switch (i)
        {
            case 0: s.nutrientBalance    = v; break;
            case 1: s.soilOrganicMatter  = v; break;
            case 2: s.soilStructure      = v; break;
            case 3: s.biologicalActivity = v; break;
            case 4: s.waterDynamics      = v; break;
            case 5: s.erosionResistance  = v; break;
        }
    }
}
