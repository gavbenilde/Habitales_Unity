using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GameEventRegistry", menuName = "Habitales/Game Event Registry")]
public class GameEventRegistry : ScriptableObject
{
    public List<GameEventSO> events = new List<GameEventSO>();
}