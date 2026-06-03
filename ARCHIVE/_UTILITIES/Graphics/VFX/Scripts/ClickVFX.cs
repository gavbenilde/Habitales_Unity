using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.VFX;

public class ClickVFX : MonoBehaviour
{
    [SerializeField] private GameObject vfx;
    [SerializeField] private Camera cam;

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
                GameObject _vfx = Instantiate(vfx, hit.point, vfx.transform.rotation);
                
                Destroy(_vfx, 1f);

                // Optional: align to surface normal
                // Instantiate(vfx, hit.point, Quaternion.LookRotation(hit.normal));
            }
        }
    }
}
