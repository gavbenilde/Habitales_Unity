using UnityEngine;

public static class ColorUtils {
    public static Color LerpHSV(Color a, Color b, float t) {
        // Convert both colors to HSV
        Color.RGBToHSV(a, out float h1, out float s1, out float v1);
        Color.RGBToHSV(b, out float h2, out float s2, out float v2);

        // Lerp each component individually
        // Use Mathf.LerpAngle for Hue if you want it to wrap around the wheel correctly
        float h = Mathf.Lerp(h1, h2, t); 
        float s = Mathf.Lerp(s1, s2, t);
        float v = Mathf.Lerp(v1, v2, t);

        // Convert back to standard Unity Color (RGB)
        return Color.HSVToRGB(h, s, v);
    }
}