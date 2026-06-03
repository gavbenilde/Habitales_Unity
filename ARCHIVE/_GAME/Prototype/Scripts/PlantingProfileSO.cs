using UnityEngine;

[CreateAssetMenu(fileName = "PlantingProfile", menuName = "Habitales/Planting Profile")]
public class PlantingProfileSO : ScriptableObject
{
    public string plantName;
    public string profileID;
    public int seed;

    // Index order matches TileStats: 0=nutrientBalance, 1=soilOrganicMatter,
    // 2=soilStructure, 3=biologicalActivity, 4=waterDynamics, 5=erosionResistance
    public float[] survivalArray = new float[6];

    public float hue;
    public float blackness;
    public int tierCount = 3;
    public int daysPerTier = 4;

    public int SpecialtyStatIndex
    {
        get
        {
            int best = 0;
            for (int i = 1; i < survivalArray.Length; i++)
                if (survivalArray[i] > survivalArray[best]) best = i;
            return best;
        }
    }

    public string SpecialtyStatName
    {
        get
        {
            switch (SpecialtyStatIndex)
            {
                case 0: return "Nutrient Balance";
                case 1: return "Soil Organic Matter";
                case 2: return "Soil Structure";
                case 3: return "Biological Activity";
                case 4: return "Water Dynamics";
                case 5: return "Erosion Resistance";
                default: return "Unknown";
            }
        }
    }
}
