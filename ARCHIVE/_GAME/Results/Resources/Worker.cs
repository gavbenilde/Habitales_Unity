using System;
using UnityEngine;

public enum WorkerTrait
{
    Charming, Witty, Boisterous, Serious, Cheerful,
    Stoic, Curious, Grumpy, Optimistic, Pragmatic
}

public enum WorkerPortraitType
{
    GeneratedInitial,
    StockPhoto
}

[Serializable]
public class Worker
{
    public string     workerName;
    public int        age;
    public int        birthdayDay;        // 1–365
    public WorkerTrait trait;
    public int        actionsParticipated;
    public bool       isFatigued;
    public int        returnDay;          // game day they become available again

    // portrait system
    public WorkerPortraitType portraitType;
    public Color              initialColor;
    public Sprite             stockPhoto;    // already added — keep as-is

    public string DisplayName => workerName;

    public string BirthdayDisplay()
    {
        int month = 1;
        int remaining = birthdayDay;
        int[] daysInMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        string[] monthNames = {
            "Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        };
        while (month <= 12 && remaining > daysInMonth[month - 1])
        {
            remaining -= daysInMonth[month - 1];
            month++;
        }
        return $"{monthNames[month - 1]} {remaining}";
    }
}