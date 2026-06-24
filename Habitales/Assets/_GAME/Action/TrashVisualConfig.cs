using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TrashVisualConfig", menuName = "Habitales/Trash Visual Config")]
public class TrashVisualConfig : ScriptableObject
{
    public List<Sprite> bioVariants;
    public List<Sprite> nonBioVariants;
}