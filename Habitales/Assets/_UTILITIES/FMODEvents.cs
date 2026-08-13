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
    [field: SerializeField] public EventReference cleanup;
    [field: SerializeField] public EventReference cardHoverEnter;
    [field: SerializeField] public EventReference cardHoverExit;
    [field: SerializeField] public EventReference trash1;
    [field: SerializeField] public EventReference trash2;
    [field: SerializeField] public EventReference trash3;
    [field: SerializeField] public EventReference timeWoosh;
    
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

    public void PlaySoundEvent(EventReference sound)
    {
        float randomPitch = Random.Range(0.9f, 1.1f);

        AudioManager.instance.PlayOneShot(sound, Vector3.zero, randomPitch);
    }
}
