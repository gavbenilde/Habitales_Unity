using UnityEngine;

public class EntityVisualizer : MonoBehaviour {
    [Header("Sprite Mappings")]

    [Space(10)] // Creates a 10-pixel gap acting as a subheader separator
    [Header("Tree Sprites")]
    [SerializeField] private Sprite seedlingTreeSprite;
    [SerializeField] private Sprite saplingTreeSprite;
    [SerializeField] private Sprite matureTreeSprite;
    [SerializeField] private Sprite deadTreeSprite;
    [SerializeField] private Sprite stumpSprite;

    [Space(10)] // Creates a 10-pixel gap acting as a subheader separator
    [Header("Village Spritese")]
    [SerializeField] private Sprite fireSprite;
    [SerializeField] private Sprite villageSprite;

    [Space(10)] // Creates a 10-pixel gap acting as a subheader separator
    [Header("Factory Sprites")]
    [SerializeField] private Sprite factorySprite;
    [SerializeField] private Sprite bioTrashSprite;

    [Header("Positioning")]
    [SerializeField] private float forwardOffset = 0.5f;

    /*VFX Temporary Test*/
    [Header("VFX")] 
    [SerializeField] private GameObject vfxPrefab;
    
    private SpriteRenderer spriteRenderer;
    private TileEntity entityData;
    private Tile parentTile;
    private Camera mainCamera;

    void Awake() {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }
        mainCamera = Camera.main;
    }

    public void Initialize(TileEntity entity, Tile tile) {
        entityData = entity;
        parentTile = tile;
        UpdateVisual();
    }

    public void UpdateVisual() {
        if (entityData == null) {
            spriteRenderer.sprite = null;
            return;
        }

        // Map entity type to sprite
        GameObject vfx;
        switch (entityData.entityType) {
            case "Seedling":
                spriteRenderer.sprite = seedlingTreeSprite;
                spriteRenderer.color = Color.white;
                break;
            case "Sapling":
                vfx = Instantiate(vfxPrefab, transform.position, transform.rotation);
                Destroy(vfx, 2f);
                spriteRenderer.sprite = saplingTreeSprite;
                spriteRenderer.color = Color.white;
                break;
            case "Mature Tree":
                vfx = Instantiate(vfxPrefab, transform.position, transform.rotation);
                Destroy(vfx, 2f);
                spriteRenderer.sprite = matureTreeSprite;
                spriteRenderer.color = Color.white;
                break;
            case "DeadTree":
                spriteRenderer.sprite = deadTreeSprite;
                spriteRenderer.color = new Color(0.6f, 0.5f, 0.4f);
                break;
            case "Stump":
                spriteRenderer.sprite = stumpSprite;
                spriteRenderer.color = Color.white;
                break;
            case "Fire":
                spriteRenderer.sprite = fireSprite;
                spriteRenderer.color = Color.white;
                break;
            case "Village":
                spriteRenderer.sprite = villageSprite;
                spriteRenderer.color = Color.white;
                break;
            default:
                Debug.LogWarning($"No sprite mapping for entity type: {entityData.entityType}");
                spriteRenderer.sprite = null;
                break;
        }

        // Position sprite so bottom sits on tile surface
        PositionSprite();
    }

    void PositionSprite() {
        if (spriteRenderer.sprite == null) return;
    
        // Calculate height based on sprite size (assuming pivot is center)
        float spriteHeight = spriteRenderer.sprite.bounds.size.y;
    
        // Get parent tile's height
        float tileHeight = 0f;
        if (transform.parent != null) {
            // Use parent's Y position or mesh bounds
            Renderer parentRenderer = transform.parent.GetComponent<Renderer>();
            if (parentRenderer != null) {
                tileHeight = parentRenderer.bounds.size.y;
            }
        }
    
        Vector3 pos = Vector3.zero;
        pos.x = forwardOffset;
        pos.y = tileHeight + (spriteHeight * 0.5f);
        pos.z = forwardOffset;
        
        transform.localPosition = pos;
    }


    void LateUpdate() {
        // Billboard: always face camera
        if (mainCamera != null) {
            transform.rotation = mainCamera.transform.rotation;
        }
    }

    public void SetEntity(TileEntity newEntity) {
        entityData = newEntity;
        UpdateVisual();
    }

    public TileEntity GetEntity() => entityData;
}
