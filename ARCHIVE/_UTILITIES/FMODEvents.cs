using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;

public class FMODEvents : MonoBehaviour
{
    [field: Header("SFX")] 
    [field: SerializeField] public EventReference tileSelectedCritical;
    [field: SerializeField] public EventReference tileSelectedHealthy;
    [field: SerializeField] public EventReference uiSelect;
    
    public static FMODEvents instance { get; private set; }
    
    // Start is called before the first frame update
    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogWarning("Found more than one FMOD Events instance in the scene.");
        }

        instance = this;
    }
}
