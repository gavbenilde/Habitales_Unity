using System;

[Serializable]
public class TileIssue
{
    public IssueType type;
    public float nutrientMult     = 1f;
    public float organicMult      = 1f;
    public float structureMult    = 1f;
    public float biologicalMult   = 1f;
    public float waterDynMult     = 1f;
    public float erosionMult      = 1f;
    public float vegetationMult   = 1f;
    public float contaminationAdd = 0f;
}