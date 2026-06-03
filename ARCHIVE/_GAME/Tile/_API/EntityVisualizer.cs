using UnityEngine;
using UnityEngine.Rendering;

public class EntityVisualizer : MonoBehaviour
{
    [Header("Tree Sprites")]
    [SerializeField] private Sprite seedlingTreeSprite;
    [SerializeField] private Sprite saplingTreeSprite;
    [SerializeField] private Sprite matureTreeSprite;
    [SerializeField] private Sprite deadTreeSprite;
    [SerializeField] private Sprite stumpSprite;

    [Header("Village / Hazard Sprites")]
    [SerializeField] private Sprite fireSprite;
    [SerializeField] private Sprite villageSprite;

    [Header("Factory Sprites")]
    [SerializeField] private Sprite factorySprite;
    [SerializeField] private Sprite bioTrashSprite;
    
    [Header("Cover Crop Sprites")]
    [SerializeField] private Sprite seedlingCCSprite;
    [SerializeField] private Sprite saplingPhytoSprite;
    [SerializeField] private Sprite matureCCSprite;
    [SerializeField] private Sprite maturePhytoSprite;

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

        switch (entityData.entityType)
        {
            case "Seedling":
                spriteRenderer.sprite = seedlingTreeSprite;
                spriteRenderer.color  = Color.white;
                break;
            case "Sapling":
                spriteRenderer.sprite = saplingTreeSprite;
                spriteRenderer.color  = Color.white;
                break;
            case "Mature Tree":
                spriteRenderer.sprite = matureTreeSprite;
                spriteRenderer.color  = Color.white;
                break;
            case "DeadTree":
                spriteRenderer.sprite = deadTreeSprite;
                spriteRenderer.color  = new Color(0.6f, 0.5f, 0.4f);
                break;
            case "Stump":
                spriteRenderer.sprite = stumpSprite;
                spriteRenderer.color  = Color.white;
                break;
            case "Fire":
                spriteRenderer.sprite = fireSprite;
                spriteRenderer.color  = Color.white;
                break;
            case "Village":
                spriteRenderer.sprite = villageSprite;
                spriteRenderer.color  = Color.white;
                break;
            case "Factory":
                spriteRenderer.sprite = factorySprite;
                spriteRenderer.color  = Color.white;
                break;
            case "TrashBio":
                spriteRenderer.sprite = bioTrashSprite;
                spriteRenderer.color  = Color.white;
                break;
            case "CoverCropSeedling":
                spriteRenderer.sprite = seedlingCCSprite;
                spriteRenderer.color  = Color.white;
                break;
            case "CoverCropSapling Phyto":
                spriteRenderer.sprite = saplingPhytoSprite;
                spriteRenderer.color  = Color.white;
                break;
            case "CoverCropMature Legume":
                spriteRenderer.sprite = matureCCSprite;
                spriteRenderer.color  = Color.white;
                break;
            case "CoverCropMature Grass":
                spriteRenderer.sprite = matureCCSprite;
                spriteRenderer.color  = Color.white;
                break;
            case "CoverCropMature Phyto":
                spriteRenderer.sprite = maturePhytoSprite;
                spriteRenderer.color  = Color.white;
                break;
            default:
                Debug.LogWarning($"EntityVisualizer: No sprite mapping for '{entityData.entityType}'");
                spriteRenderer.sprite = null;
                break;
        }
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
