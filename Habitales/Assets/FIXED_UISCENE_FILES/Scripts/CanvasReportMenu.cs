using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CanvasReportMenu : CanvasBase
{
    [Header("Tile Text Stats")]
    [SerializeField] private TextMeshProUGUI worldHealth;
    [SerializeField] private TextMeshProUGUI peakthrivingTiles;
    [SerializeField] private TextMeshProUGUI criticalTiles;
    [SerializeField] private TextMeshProUGUI degradingTiles;
    [SerializeField] private TextMeshProUGUI thrivingTiles;

    [Header("Notable Actions Text")]
    [SerializeField] private TextMeshProUGUI favoriteAction;
    [SerializeField] private TextMeshProUGUI leastusedAction;
    [SerializeField] private TextMeshProUGUI comments;

    [Header("Images")]
    [SerializeField] private Image commentStampPass;
    [SerializeField] private Image commentStampFail;
    [SerializeField] private Image zoneReport;
    [SerializeField] private Image statisticReportImage;

    public void BTN_ReturnToMain()
    {
        Debug.Log("Return To Main");
    }

    public void BTN_PlayAgain()
    {
        Debug.Log("PlayAgain");
    }

    public void SetWorldHealth(TextMeshProUGUI text)
    {

    }
}
