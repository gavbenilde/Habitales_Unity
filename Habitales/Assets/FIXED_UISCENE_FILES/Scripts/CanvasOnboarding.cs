using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public enum OnboardingPhase
{
    PlantingAction,
    Resources,
    Weather,
}

[Serializable]
public class OnboardingData
{
    public OnboardingPhase phase;
    public string titleText;
    public string bodyText;
    public GameObject animatedImage;
}

public class CanvasOnboarding : CanvasBase
{
    [SerializeField] private List<OnboardingData> onboardingData;
    
    [Header("Canvas References")]
    [SerializeField] private GameObject gifImage;
    
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI bodyText;

    private OnboardingPhase currentPhase;

    public OnboardingData GetOnboardingData(OnboardingPhase phase)
    {
        foreach (OnboardingData data in onboardingData)
        {
            if (data.phase == phase)
            {
                return data;
            }
        }
        return null;
    }

    public void SetOnboardingData(OnboardingData data)
    {
        if (data == null) return;
        ClearUI();
        
        titleText.text = data.titleText;
        bodyText.text = data.bodyText;
        gifImage = data.animatedImage;
        
        gifImage.SetActive(true);
        
        currentPhase = data.phase;
    }
    
    public void ClearUI()
    {
        titleText.text = "";
        bodyText.text = "";
        gifImage.SetActive(false);
        gifImage = null;
    }

    public void BTN_Next()
    {
        OnboardingPhase phase = currentPhase++;
        
        OnboardingData data = GetOnboardingData(phase);

        if (data == null) return;
        
        SetOnboardingData(data);
    }
    
    public void BTN_Back()
    {
        OnboardingPhase phase = currentPhase--;
        
        OnboardingData data = GetOnboardingData(phase);
        
        if (data == null) return;
        
        SetOnboardingData(data);
    }
}
