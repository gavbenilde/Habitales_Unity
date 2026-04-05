using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.VFX;

public class VFXManager : MonoBehaviour
{
    public static VFXManager Instance { get; private set; }

    [Header("Manager References")]
    [SerializeField] private TileManager tileManager;
    [SerializeField] private GameManager gameManager;
    
    [SerializeField] private List<VFXTypes> vfxList;
    
    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        
        // temp
        List<Tile> tiles = tileManager.GetAllTiles();
        
        foreach (Tile tile in tiles)
        {
            
        }
    }

    // -----------------------------
    // Basic spawn with lifetime
    // -----------------------------
    public void SpawnVFX(string key, Vector3 position, Quaternion rotation)
    {
        VFXTypes vfxData = GetVFX(key);

        if (vfxData == null) return;

        VisualEffect vfx = Instantiate(vfxData.prefab, position, rotation);

        vfx.Play();

        if (vfxData.defaultLifetime > 0)
            Destroy(vfx.gameObject, vfxData.defaultLifetime);
    }

    // -----------------------------
    // Spawn with parameters (Color, Intensity)
    // -----------------------------
    public VisualEffect SpawnVFX(VisualEffect vfxPrefab, Vector3 position, Quaternion rotation, Color color, float intensity, float lifetime = 2f)
    {
        VisualEffect vfx = Instantiate(vfxPrefab, position, rotation);

        // Set exposed parameters in VFX Graph
        vfx.SetVector4("Color", color);
        vfx.SetFloat("Intensity", intensity);

        vfx.Play();

        if (lifetime > 0)
            Destroy(vfx.gameObject, lifetime);

        return vfx;
    }

    // -----------------------------
    // Manual destroy (optional)
    // -----------------------------
    public void DestroyVFX(VisualEffect vfx, float delay = 0f)
    {
        if (vfx != null)
            Destroy(vfx.gameObject, delay);
    }
    
    private VFXTypes GetVFX(string key)
    {
        foreach (var vfx in vfxList)
        {
            if (vfx.key == key)
                return vfx;
        }

        Debug.LogWarning("VFX not found: " + key);
        return null;
    }
}

// -----------------------------
// Optional: VFX Types container
// -----------------------------
[System.Serializable]
public class VFXTypes
{
    public string key;
    public VisualEffect prefab;
    public float defaultLifetime = 2f;
}