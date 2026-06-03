using UnityEngine;

public static class HWBColor
{
    // Converts HWB (Hue, Whiteness, Blackness) to a Unity Color.
    // Algorithm: normalize out W+B if they exceed 1, then map to RGB via HSV.
    public static Color HWBToRGB(float h, float w, float b)
    {
        float total = w + b;
        if (total > 1f)
        {
            w /= total;
            b /= total;
        }

        // HWB → RGB: start from fully saturated hue, then mix in white and black
        Color hueColor = Color.HSVToRGB(h, 1f, 1f);
        float r = hueColor.r * (1f - w - b) + w;
        float g = hueColor.g * (1f - w - b) + w;
        float bl = hueColor.b * (1f - w - b) + w;
        return new Color(r * (1f - b), g * (1f - b), bl * (1f - b));
    }
}
