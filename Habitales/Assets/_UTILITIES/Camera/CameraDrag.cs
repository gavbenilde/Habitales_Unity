using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UTILITIES.Camera;

namespace _UTILITIES.Camera
{
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class CameraDrag : MonoBehaviour
    {
        [SerializeField] private float baseDragSpeed = 0.01f;
        [SerializeField] private bool invert = false;

        private UnityEngine.Camera cam;
        private Vector3 lastMousePosition;

        bool isDraggingOnEmpty = false;
        
        private void Awake()
        {
            cam = GetComponent<UnityEngine.Camera>();
        }

        void Update()
        {
            if (EventCameraHandler.Instance != null && EventCameraHandler.Instance.IsPanning) return;
            if (Input.GetMouseButtonDown(0))
            {
                isDraggingOnEmpty = CheckClickedOnNothing();
                lastMousePosition = Input.mousePosition;
            }

            if (Input.GetMouseButton(0) && isDraggingOnEmpty)
            {
                Vector3 delta = Input.mousePosition - lastMousePosition;

                float direction = invert ? 1f : -1f;

                float zoomMultiplier = cam.orthographicSize;
                float scaledSpeed = baseDragSpeed * zoomMultiplier;

                Vector3 right = transform.right;
                Vector3 forward = transform.forward;

                right.y = 0f;
                forward.y = 0f;

                right.Normalize();
                forward.Normalize();

                Vector3 move =
                    (right * delta.x + forward * delta.y) *
                    (scaledSpeed * direction);

                transform.position += move;

                lastMousePosition = Input.mousePosition;
            }
        }

        bool CheckClickedOnNothing()
        {
            if (EventSystem.current.IsPointerOverGameObject())
                return false; // treat UI as "something"
            
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            
            if (Physics.Raycast(ray, out hit))
            {
                // If object has the ignore tag → treat as "nothing"
                if (hit.collider.CompareTag("Tile"))
                    return true;

                return false; // clicked something else
            }

            return true; // clicked nothing
        }
    }
}