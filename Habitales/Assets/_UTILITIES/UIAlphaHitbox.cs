using UnityEngine;
using UnityEngine.UI;

public class UIAlphaHitbox : MonoBehaviour
{
    [Range(0f, 1f)] 
    public float alphaThreshold = 0.5f;

    private void Start()
    {
        GetComponent<Image>().alphaHitTestMinimumThreshold = alphaThreshold;
    }
}