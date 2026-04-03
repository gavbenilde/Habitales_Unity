using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

public class VFXManager : MonoBehaviour
{
    public static VFXManager Instance { get; private set; }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SpawnVFX(VisualEffect vfx, float lifetime)
    {
        VisualEffect _vfx = Instantiate(vfx);
        
        if (lifetime > 0)
            Destroy(_vfx, lifetime);
    }

    public void DestroyVFX(VisualEffect vfx)
    {
        Destroy(vfx, 1f);
    }
}

public class VFXTypes
{
    [SerializeField] private GameObject vfx;
}
