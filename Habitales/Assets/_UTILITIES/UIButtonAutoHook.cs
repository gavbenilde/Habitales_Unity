using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIButton : MonoBehaviour
{
    [SerializeField] private Camera mainCamera;
    
    // Start is called before the first frame update
    void Start()
    {
        void Start()
        {
            Button[] buttons = FindObjectsOfType<Button>();

            foreach (var btn in buttons)
            {
                btn.onClick.AddListener(() =>
                {
                    AudioManager.instance.PlayOneShot(FMODEvents.instance.uiSelect, mainCamera.transform.position);
                });
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
