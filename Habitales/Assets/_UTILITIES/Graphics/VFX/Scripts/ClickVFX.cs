using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class ClickVFX : MonoBehaviour
{
    [SerializeField] public GameObject vfxPrefab;
    [SerializeField] public Camera cam;

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current.IsPointerOverGameObject())
                return;
            
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit) && hit.collider.CompareTag("Tile"))
            {
                // Spawn at hit point
                GameObject vfx = Instantiate(vfxPrefab, hit.point, vfxPrefab.transform.rotation);
                
                Destroy(vfx, 1f);

                // Optional: align to surface normal
                // Instantiate(vfxPrefab, hit.point, Quaternion.LookRotation(hit.normal));
            }
        }
    }
}
