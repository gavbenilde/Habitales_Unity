using UnityEngine;

public static class PlantingProfileGenerator
{
    // Total budget distributed across the 6 survival stats.
    private const int BUDGET = 200;

    public static PlantingProfileSO Generate(int seed, string plantName = null)
    {
        var rng = new System.Random(seed);

        var profile = ScriptableObject.CreateInstance<PlantingProfileSO>();
        profile.profileID = $"gen_{seed}";
        profile.seed      = seed;
        profile.plantName = string.IsNullOrEmpty(plantName) ? $"Plant {seed}" : plantName;

        // Pick specialty index and assign it 70–90 of the budget.
        int specialtyIndex   = rng.Next(0, 6);
        int specialtyValue   = 70 + rng.Next(0, 21); // 70–90
        int remaining        = BUDGET - specialtyValue;

        // Distribute remaining across the other 5 stats with exponential decay.
        float[] weights = new float[5];
        float totalWeight = 0f;
        for (int i = 0; i < 5; i++)
        {
            weights[i] = Mathf.Pow(0.6f, i) * (float)(rng.NextDouble() * 0.5 + 0.75);
            totalWeight += weights[i];
        }

        float[] survivalArray = new float[6];
        survivalArray[specialtyIndex] = specialtyValue;

        int slot = 0;
        for (int i = 0; i < 6; i++)
        {
            if (i == specialtyIndex) continue;
            survivalArray[i] = Mathf.Round(remaining * (weights[slot] / totalWeight));
            slot++;
        }

        profile.survivalArray = survivalArray;
        profile.hue           = (float)rng.NextDouble();
        profile.blackness     = (float)(rng.NextDouble() * 0.3);
        profile.tierCount     = rng.Next(2, 5);    // 2–4
        profile.daysPerTier   = rng.Next(3, 6);    // 3–5

        return profile;
    }
}
