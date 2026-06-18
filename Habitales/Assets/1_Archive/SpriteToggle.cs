using UnityEngine;
using UnityEngine.UI;

public class SpriteToggle : MonoBehaviour
{
    public Image targetImage;
    public Sprite sprite1;
    public Sprite sprite2;

    private bool showingSprite1 = true;

    void Start()
    {
        targetImage.sprite = sprite1;
        targetImage.rectTransform.localScale = new Vector3(0.5f, 0.5f, 1f);
    }

    public void ToggleSprite()
    {
        if (showingSprite1)
        {
            targetImage.sprite = sprite2;
            targetImage.rectTransform.localScale = Vector3.one;
        }
        else
        {
            targetImage.sprite = sprite1;
            targetImage.rectTransform.localScale = new Vector3(0.5f, 0.5f, 1f);
        }

        showingSprite1 = !showingSprite1;
    }
}