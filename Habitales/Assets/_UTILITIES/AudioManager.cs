using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;

public class AudioManager : MonoBehaviour
{
    
    public static AudioManager instance { get; private set; }
    
    // Start is called before the first frame update
    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogError("Found more than one Audio Manager in the scene.");
        }

        instance = this;
    }

    public void PlayOneShot(EventReference sound, Vector3 worldPos)
    {
        RuntimeManager.PlayOneShot(sound, worldPos);
    }

    /// <summary>
    /// One-shot with a pitch multiplier (1 = the authored pitch; 2 = one octave up).
    ///
    /// <para>Mirrors RuntimeManager.PlayOneShot — create, place, start, release — with a setPitch
    /// in between, which the static helper gives no way to reach. Added for the tile-selection
    /// pitch ladder (2026-07-29): each tile committed within one drag plays a semitone above the
    /// last, so a six-tile stroke reads as six tiles instead of six identical blips.</para>
    ///
    /// <para>Note this is a playback-rate change, not a pitch shifter — short selection blips get
    /// correspondingly shorter as the ladder climbs, which is the intended arcade feel.</para>
    /// </summary>
    public void PlayOneShot(EventReference sound, Vector3 worldPos, float pitch)
    {
        // Cheap path when nothing is transposing it — also keeps every existing call byte-identical.
        if (Mathf.Approximately(pitch, 1f))
        {
            RuntimeManager.PlayOneShot(sound, worldPos);
            return;
        }

        FMOD.Studio.EventInstance instance = RuntimeManager.CreateInstance(sound);
        instance.set3DAttributes(RuntimeUtils.To3DAttributes(worldPos));
        instance.setPitch(pitch);
        instance.start();
        instance.release();   // fire-and-forget: FMOD frees it when playback ends
    }
}
