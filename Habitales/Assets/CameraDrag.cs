using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraDrag : MonoBehaviour
{
    [SerializeField] private float baseDragSpeed = 0.01f;
    [SerializeField] private bool invert = false;

    private Camera cam;
    private Vector3 lastMousePosition;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            lastMousePosition = Input.mousePosition;
        }

        if (Input.GetMouseButton(0))
        {
            Vector3 delta = Input.mousePosition - lastMousePosition;

            float direction = invert ? 1f : -1f;

            // Scale drag speed by zoom level
            float zoomMultiplier = cam.orthographicSize;
            float scaledSpeed = baseDragSpeed * zoomMultiplier;

            // Camera-relative directions projected to XZ
            Vector3 right = transform.right;
            Vector3 forward = transform.forward;

            right.y = 0f;
            forward.y = 0f;

            right.Normalize();
            forward.Normalize();

            Vector3 move =
                (right * delta.x + forward * delta.y) *
                scaledSpeed *
                direction;

            transform.position += move;

            lastMousePosition = Input.mousePosition;
        }
    }
}