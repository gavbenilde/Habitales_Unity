using System.Collections.Generic;
using UnityEngine;

public class PlantingAction : PlayerAction
{
    // TODO (tile-pivot friction): Tiles are at 0.55 scale and their mesh pivot sits at the
    // bottom. We apply the lift as localPosition.y so the inspector displays the value
    // verbatim (a world-Y bump showed up as 4.18 in the inspector — confusing). Effective
    // world height = EntityYOffset × parent scale; tune the value visually, don't try to
    // back-solve it from world units. The proper fix is to normalize tile pivots (or expose
    // an EntityAnchor transform on the visualizer). Revisit when the tile-scale pass happens.
    private const float EntityYOffset = 2.4f;

    private readonly PlantingProfileSO profile;
    private readonly PlayerProgressionSO progression;

    // Base material for the spawned cube. See ExecuteOnTile for why the
    // primitive's runtime-default material can't be relied on in builds.
    private readonly Material cubeMaterial;

    public PlantingAction(PlantingProfileSO p, Material cubeMat, PlayerProgressionSO prog = null)
    {
        profile      = p;
        cubeMaterial = cubeMat;
        progression  = prog;
    }

    public PlantingProfileSO Profile            => profile;
    public override ActionCategory Category     => ActionCategory.Intervene;
    public override string ActionName           => $"Plant {profile.plantName}";
    public override string Description          => $"This plant survives extreme {profile.SpecialtyStatName}.";
    public override SelectionMode selectionMode => SelectionMode.FloodFill;
    public override int MinPeoplePerTile        => 1;
    public override int BaseDays               => 2;
    public override int MinDays                => 1;
    public override string VariantGroupName    => "Planting";

    public override bool CanExecute(List<Tile> tiles)
    {
        if (tiles == null || tiles.Count == 0) return false;
        foreach (Tile t in tiles)
            if (t.entity == null) return true;
        return false;
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile.entity != null) return;

        tileManager.SpawnEntity<PlantedCubeEntity>(tile);
        var cubeEnt = (PlantedCubeEntity)tile.entity;
        cubeEnt.profile = profile;

        var visGO = tileManager.GetEntityVisualizer(tile)?.gameObject;
        if (visGO != null)
        {
            // Lift the EntityVisualizer (Entity_PlantedCube) via localPosition so the inspector
            // shows EntityYOffset verbatim. See EntityYOffset comment for why we don't use world Y.
            Vector3 lp = visGO.transform.localPosition;
            visGO.transform.localPosition = new Vector3(lp.x, EntityYOffset, lp.z);

            var cubeGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeGO.name = "PlantCube";
            cubeGO.transform.SetParent(visGO.transform, worldPositionStays: false);
            cubeGO.transform.localPosition = Vector3.zero;
            Object.Destroy(cubeGO.GetComponent<Collider>());

            // CreatePrimitive assigns Unity's built-in runtime-default material.
            // The build dependency scanner does not track that shader, so the cube
            // renders fine in the Editor but turns magenta/invisible in builds.
            // cubeMaterial is a real serialized asset reference (set on ActionManager),
            // so Unity bundles its shader. We assign it as sharedMaterial; RefreshVisual's
            // `.material` access then instances a per-cube copy for the HWB color.
            if (cubeMaterial != null)
                cubeGO.GetComponent<MeshRenderer>().sharedMaterial = cubeMaterial;
            else
                Debug.LogWarning("[PlantingAction] No Plant Cube Material assigned on " +
                    "ActionManager — planted cubes fall back to the runtime-default " +
                    "shader, which is stripped from builds.");

            cubeEnt.cubeTransform = cubeGO.transform;
            cubeEnt.RefreshVisual(tile);
        }

        if (progression != null)
        {
            bool firstEver    = !progression.plantedEverIds.Contains(profile.profileID);
            bool firstSession = !progression.plantedThisSessionIds.Contains(profile.profileID);

            if (firstEver)
            {
                Habitales.Dialogue.DialogueManager.Instance?.AppendAziMessage(
                    $"This is {profile.plantName}! They can survive extreme {profile.SpecialtyStatName}.");
                progression.plantedEverIds.Add(profile.profileID);
            }
            else if (firstSession)
            {
                Habitales.Dialogue.DialogueManager.Instance?.AppendAziMessage(
                    $"Welcome back to {profile.plantName}. Remember — they thrive in {profile.SpecialtyStatName}.");
            }

            if (firstSession)
                progression.plantedThisSessionIds.Add(profile.profileID);
        }
    }
}
