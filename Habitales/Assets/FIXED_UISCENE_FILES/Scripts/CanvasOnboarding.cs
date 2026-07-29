using System;
using System.Collections;
using System.Collections.Generic;
using Habitales.UI;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public enum OnboardingPhase
{
    Controls,
    PlantingAction,
    Resources,
    WorkerFatigue,
    Weather,
    Inspector,
    CleanupAction,
    DeathPings,
    EndReport,
    ZoneUnlocked,
    Factory,
    Contamination
}

[Serializable]
public class OnboardingData
{
    public OnboardingPhase phase;
    public string titleText;
    [TextArea(3, 10)] public string bodyText;
    public GameObject animatedImage;
}

public class CanvasOnboarding : CanvasBase
{
    [SerializeField] private PauseMenuController pauseMenuController;
    
    [SerializeField] private List<OnboardingData> onboardingData;
    
    [Header("Canvas References")]
    [SerializeField] private GameObject gifImage;
    
    [SerializeField] private TextMeshProUGUI title;
    [SerializeField] private TextMeshProUGUI body;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button backButton;

    private OnboardingPhase currentPhase;

    void Start()
    {
        SetOnboardingData(onboardingData[0]);
    }
    
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
        if (data == null)
            return;
        
        ClearUI();
        
        title.text = data.titleText;
        body.text = data.bodyText;
        gifImage = data.animatedImage;
        
        if (gifImage != null)
            gifImage.SetActive(true);
        
        currentPhase = data.phase;
        
        OnboardingPhase nextPhase = currentPhase + 1;
        OnboardingPhase prevPhase = currentPhase - 1;
        
        nextButton.interactable = GetOnboardingData(nextPhase) != null;
        backButton.interactable = GetOnboardingData(prevPhase) != null;
    }
    
    public void ClearUI()
    {
        if (title != null)
            title.text = "";
        
        if (body != null)
            body.text = "";

        if (gifImage != null)
        {
            gifImage.SetActive(false);
            gifImage = null;
        }
    }

    public void BTN_Next()
    {
        // if (currentPhase == OnboardingPhase.EndReport)
        //     return;

        OnboardingPhase phase = currentPhase + 1;
        
        OnboardingData data = GetOnboardingData(phase);

        if (data == null)
            return;   
        
        SetOnboardingData(data);
    }
    
    public void BTN_Back()
    {
        // if (currentPhase == OnboardingPhase.EndReport)
        //     return;
        
        OnboardingPhase phase = currentPhase - 1;
        
        OnboardingData data = GetOnboardingData(phase);
        
        if (data == null)
            return;   
        
        SetOnboardingData(data);
    }

    public void ResumeInsideOnboarding()
    {
        pauseMenuController.Resume();
    }
}
