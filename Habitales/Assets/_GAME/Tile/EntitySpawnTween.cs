using UnityEngine;

// EntitySpawnTween — cosmetic-only pop-in for freshly spawned/promoted entity visuals (arch S3:
// presentation-only, never touches sim state). Fires from TileManager's spawn paths ONLY
// (SpawnFromDef fresh-spawn + ReplaceWithSO stage-promotion); RefreshAllVisuals never calls this
// (it doesn't touch EntityVisualizer at all — see TileManager.UpdateTileVisual), so the daily
// bulk visual sync can never re-trigger or interrupt a pop.
public static class EntitySpawnTween
{
    /// <summary>
    /// Scales <paramref name="target"/> from zero up to <paramref name="finalScale"/> with a single
    /// clean overshoot-and-settle (ease-out-back — snappy like easeOutElastic but without the
    /// bounce/wobble). Cancel-safe: clears any in-flight tween on this GameObject first, and the tween
    /// itself is naturally destroy-safe (LeanTween drops tweens on a destroyed target).
    /// </summary>
    public static void PopIn(GameObject target, Vector3 finalScale, float duration, float overshoot)
    {
        if (target == null) return;

        // Cancel-safe: a stage-promotion pop can land while an earlier pop on the same GameObject
        // is still mid-flight (e.g. rapid re-promotion) — clear it before starting a fresh one so
        // the two never fight over localScale.
        LeanTween.cancel(target);

        target.transform.localScale = Vector3.zero;
        LeanTween.scale(target, finalScale, duration)
            .setEase(LeanTweenType.easeOutBack)
            .setOvershoot(overshoot);
    }
}
