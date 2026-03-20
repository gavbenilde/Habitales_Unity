using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VFXSpawner : MonoBehaviour
{
    public GameObject vfxPrefab;
    public Transform spawnPoint;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F)) // change key as needed
        {
            SpawnVFX();
        }
    }

    void SpawnVFX()
    {
        GameObject vfx = Instantiate(vfxPrefab, spawnPoint.position, vfxPrefab.transform.rotation);
        Destroy(vfx, 2f); // adjust duration
    }
}
