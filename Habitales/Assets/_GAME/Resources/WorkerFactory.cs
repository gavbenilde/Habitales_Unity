using System.Collections.Generic;
using UnityEngine;
using Habitales;

public static class WorkerFactory
{
    public static WorkerPortraitPool portraitPool;
    
    private static void AssignPortrait(Worker worker)
    {
        bool useStock = portraitPool != null
                        && portraitPool.stockPhotos != null
                        && portraitPool.stockPhotos.Count > 0
                        && Random.value <= 0.60f;

        if (useStock)
        {
            worker.portraitType = WorkerPortraitType.StockPhoto;
            worker.stockPhoto   = portraitPool.GetNextPortrait();
            worker.initialColor = Color.white;
        }
        else
        {
            worker.portraitType = WorkerPortraitType.GeneratedInitial;
            worker.stockPhoto   = null;
            worker.initialColor = Color.HSVToRGB(Random.value, 0.55f, 0.85f);
        }
    }
    
    private static readonly string[] FirstNames =
    {
        "Aling", "Mang", "Nena", "Rodel", "Lita", "Cris", "Tito",
        "Nora", "Ferdie", "Luz", "Bong", "Marites", "Jun", "Perla",
        "Dodong", "Tessie", "Ramon", "Gloria", "Dante", "Belen",
        "Pedring", "Isang", "Nonoy", "Virgie", "Arman", "Celing",
        "Totoy", "Nenita", "Rudy", "Flor"
    };

    private static readonly string[] LastNames =
    {
        "Santos", "Reyes", "Cruz", "Garcia", "Dela Cruz", "Ramos",
        "Mendoza", "Torres", "Villanueva", "Castillo", "Flores",
        "Aquino", "Bautista", "Ocampo", "Morales", "Lim", "Pascual",
        "Soriano", "Domingo", "Velasco"
    };

    private const int TRAIT_COUNT = 10; // must match WorkerTrait enum count

    public static Worker Generate()
    {
        return new Worker
        {
            workerName          = $"{FirstNames[Random.Range(0, FirstNames.Length)]} {LastNames[Random.Range(0, LastNames.Length)]}",
            age                 = Random.Range(18, 61),
            birthdayDay         = Random.Range(1, 366),
            trait               = (WorkerTrait)Random.Range(0, TRAIT_COUNT),
            actionsParticipated = 0,
            isFatigued          = false,
            returnDay           = 0
        };
    }

    public static List<Worker> GenerateBatch(int count)
    {
        var workers = new List<Worker>();
        for (int i = 0; i < count; i++)
            workers.Add(Generate());
        return workers;
    }
}