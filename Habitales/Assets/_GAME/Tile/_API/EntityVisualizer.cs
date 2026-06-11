using UnityEngine;
using UnityEngine.Rendering;

// EntityVisualizer — billboard sprite for a tile's entity. Post-Phase-4 the sprite is fully
// data-driven: it comes from the entity's TileEntitySO `def.tileSprite` (arch §5.1). The old
// per-type Sprite fields + entityType string-switch are gone with the legacy entities.
public class EntityVisualizer : MonoBehaviour
{
    [Header("Positioning")]
    [SerializeField] private float forwardOffset = 0.5f;
    [SerializeField] private float heightOffset = 0.5f;

    [Header("Material")]
    [SerializeField] private Material defaultMaterial; // assign a Lit material in Inspector

    private SpriteRenderer spriteRenderer;
    private TileEntity     entityData;
    private Tile           parentTile;
    private Camera         mainCamera;

    public void SetSprite(Sprite s) => spriteRenderer.sprite = s;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();

        if (defaultMaterial != null)
            spriteRenderer.material = defaultMaterial;

        spriteRenderer.shadowCastingMode = ShadowCastingMode.On;
        spriteRenderer.receiveShadows = true;

        mainCamera = Camera.main;
    }

    public void Initialize(TileEntity entity, Tile tile)
    {
        entityData = entity;
        parentTile = tile;
        UpdateVisual();
    }

    public void UpdateVisual()
    {
        if (entityData == null) { spriteRenderer.sprite = null; return; }

        // Data-driven (arch §5.1): the SO carries the sprite.
        spriteRenderer.sprite = entityData.def != null ? entityData.def.tileSprite : null;
        spriteRenderer.color  = Color.white;
        if (entityData.def == null)
            Debug.LogWarning("EntityVisualizer: entity has no def — cannot resolve a sprite.");
        else if (entityData.def.tileSprite == null)
            Debug.LogWarning($"EntityVisualizer: entity '{entityData.def.entityId}' has no tileSprite assigned.", entityData.def);

        spriteRenderer.shadowCastingMode = ShadowCastingMode.On;
        spriteRenderer.receiveShadows = true;
        spriteRenderer.flipX = true;

        PositionSprite();
    }

    void PositionSprite()
    {
        if (spriteRenderer.sprite == null) return;
        float spriteHeight = spriteRenderer.sprite.bounds.size.y;
        float tileHeight   = 0f;
        if (transform.parent != null)
        {
            Renderer parentRenderer = transform.parent.GetComponent<Renderer>();
            if (parentRenderer != null) tileHeight = parentRenderer.bounds.size.y;
        }
        transform.localPosition = new Vector3(
            (forwardOffset * 1.2f),
            tileHeight + spriteHeight * heightOffset,
            forwardOffset);
    }

    void LateUpdate()
    {
        if (mainCamera != null)
            transform.rotation = mainCamera.transform.rotation;
    }

    public void SetEntity(TileEntity newEntity)
    {
        entityData = newEntity;
        UpdateVisual();
    }

    public TileEntity GetEntity() => entityData;
}
