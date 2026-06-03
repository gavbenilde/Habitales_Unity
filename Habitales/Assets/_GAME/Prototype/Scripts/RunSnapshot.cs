using System.Collections;
using UnityEngine;

/// <summary>
/// Owns the peak-thriving snapshot for a run: the data (count, screenshot, day)
/// AND the act of capturing it. RunManager hosts the coroutine; logic lives here.
/// EndGameData holds a reference to this object — it does not copy fields.
/// </summary>
public class RunSnapshot
{
    public int       peakThrivingCount;
    public Texture2D peakScreenshot;
    public int       peakAtDay;

    public bool TryRecordPeak(int currentThriving, int currentDay)
    {
        if (currentThriving <= peakThrivingCount) return false;
        peakThrivingCount = currentThriving;
        peakAtDay         = currentDay;
        return true;
    }

    // TODO (production): captures HUD too. For prod, render the world camera to a
    // RenderTexture separately so the snapshot is HUD-free.
    public IEnumerator CaptureRoutine()
    {
        yield return new WaitForEndOfFrame();
        if (peakScreenshot != null) Object.Destroy(peakScreenshot);
        peakScreenshot = ScreenCapture.CaptureScreenshotAsTexture();
    }

    public void Dispose()
    {
        if (peakScreenshot != null) Object.Destroy(peakScreenshot);
        peakScreenshot = null;
    }
}
