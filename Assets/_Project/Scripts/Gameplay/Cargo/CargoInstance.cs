using UnityEngine;
using UnityEngine.Serialization;

public enum CargoType
{
    Generic,
    Glass,
    Fruit,
    Box
}

public enum CargoBreakStyle
{
    DisableObject,
    FlattenAndRemain,
    BruiseAndRemain
}

public enum CargoState
{
    Free,
    Carried,
    Loaded
}

[DisallowMultipleComponent]
public class CargoInstance : MonoBehaviour, IInteractable
{
    private const float LoadSpawnPadding = 0.02f;
    private const float LoadImpactGracePeriod = 0.2f;
    private const float LoadedColliderContactOffset = 0.001f;

    [Header("Cargo")]
    [SerializeField] private CargoData data;
    [SerializeField] private CargoState state = CargoState.Free;
    [SerializeField, HideInInspector] private string cargoName = "Cargo";
    [SerializeField, HideInInspector] private CargoType cargoType = CargoType.Generic;
    [SerializeField, HideInInspector, Min(0.1f)] private float mass = 5f;
    [SerializeField, Range(0f, 5f)] private float fragility = 1f;
    [SerializeField] private bool canBeCarried = true;
    [SerializeField] private CargoBreakStyle breakStyle = CargoBreakStyle.DisableObject;

    [Header("References")]
    [FormerlySerializedAs("itemRigidbody")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private CargoPhysicsSettings physicsSettings;
    [SerializeField] private Durability durability;
    [SerializeField] private WorldDurabilityLabel durabilityLabel;
    [SerializeField] private Collider[] itemColliders;
    [SerializeField] private Renderer[] cargoRenderers;

    [Header("Visuals")]
    [SerializeField] private Color healthyColor = Color.white;
    [SerializeField] private Color damagedColor = new Color(0.95f, 0.65f, 0.35f, 1f);
    [SerializeField] private Color destroyedColor = new Color(0.2f, 0.1f, 0.1f, 1f);

    private bool _worldUseGravity = true;
    private bool _worldDetectCollisions = true;
    private float _worldLinearDamping;
    private float _worldAngularDamping;
    private int _originalLayer;
    private PlayerCarryController _carrier;
    private CharacterController _ignoredPlayerCollider;
    private CartCargoZone _currentZone;
    private CartInventory _loadedInventory;
    private Behaviour _cargoPhysicsBehaviour;
    private CargoFallDetector _fallDetector;
    private bool _presentationHidden;
    private Vector3 _defaultLocalScale;
    private bool _defaultCanBeCarried;

    public CargoData Data => data;
    public string CargoName => data != null ? data.CargoName : cargoName;
    public Vector3Int GridSize => data != null ? data.GridSize : Vector3Int.one;
    public float Mass => data != null ? data.Weight : mass;
    public float PhysicalMass => Mathf.Max(0.01f, Mass * PhysicsSettings.CargoMassMultiplier);
    public float Fragility => fragility;
    public CargoType Type => cargoType;
    public CargoState State => state;
    public bool IsFree => state == CargoState.Free;
    public bool IsCarried => state == CargoState.Carried;
    public bool IsLoaded => state == CargoState.Loaded;
    public bool ShouldUseCargoPhysics => state != CargoState.Carried;
    public bool IsBroken => durability != null && durability.IsDestroyed;
    public Rigidbody ItemRigidbody => rb;
    public CartCargoZone CurrentZone => _currentZone;
    public CartInventory LoadedInventory => _loadedInventory;
    public Durability Durability => durability != null ? durability : (durability = GetComponent<Durability>());
    public float CurrentHp => Durability != null ? Durability.CurrentDurability : MaxHp;
    public float MaxHp => Durability != null ? Durability.MaxDurability : 1f;
    public float HpPercent => Durability != null ? Durability.NormalizedDurability : 1f;
    public CargoPhysicsSettings PhysicsSettings => physicsSettings != null ? physicsSettings : CargoPhysicsSettings.Load();
    public bool IsPresentationHidden => _presentationHidden;

    private void Awake()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        if (durability == null)
        {
            durability = GetComponent<Durability>();
        }

        if (itemColliders == null || itemColliders.Length == 0)
        {
            itemColliders = GetComponentsInChildren<Collider>(true);
        }

        if (cargoRenderers == null || cargoRenderers.Length == 0)
        {
            cargoRenderers = GetComponentsInChildren<Renderer>(true);
        }

        physicsSettings = PhysicsSettings;

        if (durabilityLabel == null)
        {
            durabilityLabel = GetComponent<WorldDurabilityLabel>();
        }

        if (durabilityLabel == null)
        {
            durabilityLabel = gameObject.AddComponent<WorldDurabilityLabel>();
        }

        CacheWorldPhysicsDefaults();
        CacheCargoPhysicsBehaviour();
        EnsureFallDetector();
        _defaultLocalScale = transform.localScale;
        _defaultCanBeCarried = canBeCarried;
        _originalLayer = gameObject.layer;

        ApplyCargoWeight();
        ApplyColliderMaterial();
        ApplyStatePhysics();
        durabilityLabel.Initialize(durability, cargoRenderers);
    }

    private void OnEnable()
    {
        if (durability != null)
        {
            durability.DurabilityChanged += HandleDurabilityChanged;
            durability.Destroyed += HandleDestroyed;
        }

        ApplyCargoWeight();
        ApplyColliderMaterial();
        SyncCargoPhysicsBehaviour();
        UpdateVisualState();
    }

    private void OnDisable()
    {
        if (durability != null)
        {
            durability.DurabilityChanged -= HandleDurabilityChanged;
            durability.Destroyed -= HandleDestroyed;
        }

        if (_loadedInventory != null)
        {
            _loadedInventory.RemoveCargo(this);
        }

        if (_currentZone != null)
        {
            _currentZone.ForceRemove(this);
        }

        PlayerCarryController activeCarrier = _carrier;
        ReleaseCarrierInteractions();

        if (activeCarrier != null)
        {
            activeCarrier.NotifyCarriedItemUnavailable(this);
        }
    }

    public bool CanInteract(PlayerInteractor interactor)
    {
        if (interactor == null || IsBroken || !canBeCarried)
        {
            return false;
        }

        TryRecoverLostLoadedState();

        PlayerCarryController carryController = interactor.CarryController;
        return carryController != null && !carryController.IsCarrying && state == CargoState.Free;
    }

    public void Interact(PlayerInteractor interactor)
    {
        interactor?.CarryController?.TryPickUp(this);
    }

    public string GetInteractionLabel(PlayerInteractor interactor)
    {
        return $"Pick up {CargoName}";
    }

    public void PickUp()
    {
        SetState(CargoState.Carried);
    }

    public void Drop()
    {
        Drop(transform.position, transform.rotation, Vector3.zero);
    }

    public void Drop(Vector3 worldPosition, Quaternion worldRotation, Vector3 releaseVelocity)
    {
        if (_loadedInventory != null)
        {
            _loadedInventory.RemoveCargo(this);
        }

        if (_currentZone != null)
        {
            _currentZone.ForceRemove(this);
        }

        SetLoadedInventory(null);
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(worldPosition, worldRotation);
        RestoreDefaultScale();
        ApplyFreePhysics(releaseVelocity);
        ReleaseCarrierInteractions();
        SetCarryLayer(false);
        SetPresentationHidden(false);
        SetState(CargoState.Free);
    }

    public void LoadIntoCart(Transform parent, Vector3 localPos)
    {
        LoadIntoCart(null, parent, localPos, _defaultLocalScale);
    }

    public void LoadIntoCart(Transform parent, Vector3 localPos, Vector3 localScale)
    {
        LoadIntoCart(null, parent, localPos, localScale);
    }

    public void LoadIntoCart(CartInventory inventory, Transform parent, Vector3 localPos)
    {
        LoadIntoCart(inventory, parent, localPos, _defaultLocalScale);
    }

    public void LoadIntoCart(CartInventory inventory, Transform parent, Vector3 localPos, Vector3 localScale)
    {
        if (parent == null)
        {
            return;
        }

        if (_currentZone != null && _currentZone != inventory?.CargoZone)
        {
            _currentZone.ForceRemove(this);
        }

        ReleaseCarrierInteractions();
        SetCarryLayer(false);
        SetLoadedInventory(inventory);

        transform.SetParent(null, true);

        // Grid placement defines the initial pose and intended footprint,
        // but the box remains a free Rigidbody after spawning.
        transform.localScale = localScale;
        float loadPadding = inventory != null ? 0f : LoadSpawnPadding;
        Vector3 worldPosition = parent.TransformPoint(localPos) + (parent.up * loadPadding);
        Quaternion worldRotation = parent.rotation;
        Rigidbody carrierBody = ResolveCarrierBody(parent);
        Vector3 inheritedVelocity = carrierBody != null
            ? (inventory != null
                ? Vector3.ProjectOnPlane(carrierBody.linearVelocity, parent.up)
                : carrierBody.GetPointVelocity(worldPosition))
            : Vector3.zero;

        if (rb != null)
        {
            ApplyCargoWeight();
            ApplyColliderMaterial();
            rb.useGravity = _worldUseGravity;
            rb.isKinematic = false;
            rb.detectCollisions = _worldDetectCollisions;
            rb.linearDamping = PhysicsSettings.CargoDrag;
            rb.angularDamping = PhysicsSettings.CargoAngularDrag;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = inheritedVelocity;
            rb.angularVelocity = Vector3.zero;
            rb.position = worldPosition;
            rb.rotation = worldRotation;
            rb.WakeUp();
        }
        else
        {
            transform.SetPositionAndRotation(worldPosition, worldRotation);
        }

        IgnoreContainerSpawnImpacts(parent);
        SetPresentationHidden(false);
        SetState(CargoState.Loaded);
    }

    public void UnloadFromCart()
    {
        if (_currentZone != null)
        {
            _currentZone.ForceRemove(this);
        }

        SetLoadedInventory(null);
        transform.SetParent(null, true);
        RestoreDefaultScale();
        ApplyFreePhysics(Vector3.zero);
        SetPresentationHidden(false);
        SetState(CargoState.Free);
    }

    public void HandleJointBreak()
    {
        if (state == CargoState.Loaded)
        {
            MarkLostFromCart();
            return;
        }

        SetState(CargoState.Free);
    }

    public bool TryBeginCarry(PlayerCarryController carrier, CharacterController playerCollider)
    {
        if (carrier == null || IsBroken || IsCarried || !canBeCarried || rb == null)
        {
            return false;
        }

        TryRecoverLostLoadedState();
        if (IsLoaded)
        {
            return false;
        }

        if (_currentZone != null)
        {
            _currentZone.ForceRemove(this);
        }

        _carrier = carrier;
        _ignoredPlayerCollider = playerCollider;
        SetLoadedInventory(null);
        RestoreDefaultScale();

        PickUp();

        rb.useGravity = false;
        rb.isKinematic = true;
        rb.detectCollisions = false;
        rb.linearDamping = Mathf.Max(10f, _worldLinearDamping);
        rb.angularDamping = Mathf.Max(12f, _worldAngularDamping);
        rb.WakeUp();

        SetIgnoredWithPlayer(playerCollider, true);
        SetCarryLayer(true);
        return true;
    }

    public void EndCarry(PlayerCarryController carrier, CharacterController playerCollider, Vector3 worldPosition, Quaternion worldRotation, Vector3 releaseVelocity)
    {
        if (carrier != null && _carrier != carrier)
        {
            return;
        }

        Drop(worldPosition, worldRotation, releaseVelocity);
    }

    public void SetCurrentZone(CartCargoZone zone)
    {
        _currentZone = zone;
    }

    public void SetLoadedInventory(CartInventory inventory)
    {
        _loadedInventory = inventory;
    }

    public void ResetForLevel(Vector3 worldPosition, Quaternion worldRotation)
    {
        gameObject.SetActive(true);
        canBeCarried = _defaultCanBeCarried;
        Drop(worldPosition, worldRotation, Vector3.zero);
        Durability?.ResetDurability();
        SetPresentationHidden(false);

        if (rb != null)
        {
            ApplyCargoWeight();
            ApplyColliderMaterial();
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void TryRecoverLostLoadedState()
    {
        if (state != CargoState.Loaded)
        {
            return;
        }

        if (_loadedInventory == null)
        {
            MarkLostFromCart();
            return;
        }

        float interactionPadding = Mathf.Max(0.05f, _loadedInventory.CellWorldSize * 0.35f);
        if (!_loadedInventory.IsInsideTrackedVolume(this, interactionPadding))
        {
            _loadedInventory.HandleCargoLost(this);
        }
    }

    public void MarkLostFromCart()
    {
        if (_currentZone != null)
        {
            _currentZone.ForceRemove(this);
        }

        SetLoadedInventory(null);
        RestoreDefaultScale();

        if (rb != null)
        {
            ApplyCargoWeight();
            ApplyColliderMaterial();
            rb.useGravity = _worldUseGravity;
            rb.isKinematic = false;
            rb.detectCollisions = _worldDetectCollisions;
            rb.linearDamping = PhysicsSettings.CargoDrag;
            rb.angularDamping = PhysicsSettings.CargoAngularDrag;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.WakeUp();
        }

        SetPresentationHidden(false);
        SetState(CargoState.Free);
    }

    public void SetPresentationHidden(bool hidden)
    {
        _presentationHidden = hidden;

        if (cargoRenderers != null)
        {
            for (int i = 0; i < cargoRenderers.Length; i++)
            {
                if (cargoRenderers[i] == null)
                {
                    continue;
                }

                cargoRenderers[i].enabled = !hidden;
            }
        }

        if (durabilityLabel != null)
        {
            durabilityLabel.enabled = !hidden;
        }
    }

    public bool TryGetGridPlacement(
        Transform parent,
        Vector3 blockMinLocal,
        out Vector3 localPosition,
        out Vector3 localScale)
    {
        return CargoGridPlacementUtility.TryCalculatePlacement(
            transform,
            itemColliders,
            cargoRenderers,
            parent,
            blockMinLocal,
            _defaultLocalScale,
            out localPosition,
            out localScale);
    }

    public bool TryGetBoundsInSpace(Transform referenceSpace, out Bounds bounds)
    {
        return CargoGridPlacementUtility.TryGetBoundsInSpace(
            transform,
            itemColliders,
            cargoRenderers,
            referenceSpace,
            out bounds);
    }

    private void HandleDurabilityChanged(Durability currentDurability)
    {
        UpdateVisualState();
    }

    private void HandleDestroyed(Durability currentDurability)
    {
        UpdateVisualState();

        if (_currentZone != null)
        {
            _currentZone.ForceRemove(this);
        }

        if (_carrier != null)
        {
            PlayerCarryController activeCarrier = _carrier;
            ReleaseCarrierInteractions();
            SetCarryLayer(false);
            activeCarrier.NotifyCarriedItemUnavailable(this);
        }

        switch (breakStyle)
        {
            case CargoBreakStyle.FlattenAndRemain:
                Vector3 scale = transform.localScale;
                transform.localScale = new Vector3(scale.x, scale.y * 0.35f, scale.z);
                canBeCarried = false;
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.linearDamping = Mathf.Max(rb.linearDamping, 5f);
                }
                break;

            case CargoBreakStyle.BruiseAndRemain:
                canBeCarried = false;
                if (rb != null)
                {
                    rb.linearDamping = Mathf.Max(rb.linearDamping, 4f);
                }
                break;

            case CargoBreakStyle.DisableObject:
            default:
                gameObject.SetActive(false);
                break;
        }
    }

    private void UpdateVisualState()
    {
        Color targetColor = healthyColor;

        if (durability != null)
        {
            if (durability.IsDestroyed)
            {
                targetColor = destroyedColor;
            }
            else
            {
                targetColor = Color.Lerp(damagedColor, healthyColor, durability.NormalizedDurability);
            }
        }

        for (int i = 0; i < cargoRenderers.Length; i++)
        {
            ApplyTint(cargoRenderers[i], targetColor);
        }
    }

    private void CacheWorldPhysicsDefaults()
    {
        if (rb == null)
        {
            return;
        }

        _worldUseGravity = rb.useGravity;
        _worldDetectCollisions = rb.detectCollisions;
        _worldLinearDamping = rb.linearDamping;
        _worldAngularDamping = rb.angularDamping;
    }

    private void RestoreDefaultScale()
    {
        transform.localScale = _defaultLocalScale;
    }

    private void CacheCargoPhysicsBehaviour()
    {
        if (_cargoPhysicsBehaviour != null)
        {
            return;
        }

        _cargoPhysicsBehaviour = GetComponent("CargoPhysics") as Behaviour;
    }

    private void EnsureFallDetector()
    {
        if (_fallDetector == null)
        {
            _fallDetector = GetComponent<CargoFallDetector>();
        }

        if (_fallDetector == null)
        {
            _fallDetector = gameObject.AddComponent<CargoFallDetector>();
        }

        _fallDetector.Initialize(this);
    }

    private void ApplyCargoWeight()
    {
        if (rb != null)
        {
            rb.mass = PhysicalMass;
            rb.maxAngularVelocity = 20f;
        }
    }

    private void ApplyColliderMaterial()
    {
        PhysicsMaterial cargoMaterial = PhysicsSettings.GetOrCreateCargoMaterial();
        if (itemColliders == null)
        {
            return;
        }

        for (int i = 0; i < itemColliders.Length; i++)
        {
            Collider itemCollider = itemColliders[i];
            if (itemCollider == null || itemCollider.isTrigger)
            {
                continue;
            }

            itemCollider.sharedMaterial = cargoMaterial;
            itemCollider.contactOffset = LoadedColliderContactOffset;
        }
    }

    private void ApplyStatePhysics()
    {
        switch (state)
        {
            case CargoState.Carried:
                if (rb != null)
                {
                    rb.useGravity = false;
                    rb.isKinematic = true;
                    rb.detectCollisions = false;
                }
                break;

            case CargoState.Loaded:
                if (rb != null)
                {
                    rb.useGravity = _worldUseGravity;
                    rb.isKinematic = false;
                    rb.detectCollisions = _worldDetectCollisions;
                    rb.linearDamping = PhysicsSettings.CargoDrag;
                    rb.angularDamping = PhysicsSettings.CargoAngularDrag;
                    rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                }
                break;

            case CargoState.Free:
            default:
                ApplyFreePhysics(Vector3.zero);
                break;
        }
    }

    private void ApplyFreePhysics(Vector3 releaseVelocity)
    {
        if (rb == null)
        {
            return;
        }

        rb.useGravity = _worldUseGravity;
        rb.isKinematic = false;
        rb.detectCollisions = _worldDetectCollisions;
        rb.linearDamping = PhysicsSettings.CargoDrag > 0f ? PhysicsSettings.CargoDrag : _worldLinearDamping;
        rb.angularDamping = PhysicsSettings.CargoAngularDrag > 0f ? PhysicsSettings.CargoAngularDrag : _worldAngularDamping;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearVelocity = releaseVelocity;
        rb.angularVelocity = Vector3.zero;
        rb.WakeUp();
    }

    private void ReleaseCarrierInteractions()
    {
        if (_ignoredPlayerCollider != null)
        {
            SetIgnoredWithPlayer(_ignoredPlayerCollider, false);
            _ignoredPlayerCollider = null;
        }

        _carrier = null;
    }

    private void SetState(CargoState newState)
    {
        state = newState;
        SyncCargoPhysicsBehaviour();
    }

    private void SyncCargoPhysicsBehaviour()
    {
        CacheCargoPhysicsBehaviour();

        if (_cargoPhysicsBehaviour != null)
        {
            _cargoPhysicsBehaviour.enabled = state != CargoState.Carried;
        }
    }

    private void SetIgnoredWithPlayer(CharacterController playerCollider, bool ignored)
    {
        if (playerCollider == null || itemColliders == null)
        {
            return;
        }

        for (int i = 0; i < itemColliders.Length; i++)
        {
            Collider itemCollider = itemColliders[i];
            if (itemCollider == null)
            {
                continue;
            }

            Physics.IgnoreCollision(playerCollider, itemCollider, ignored);
        }
    }

    private void SetCarryLayer(bool carried)
    {
        gameObject.layer = carried ? Physics.IgnoreRaycastLayer : _originalLayer;
    }

    private static Rigidbody ResolveCarrierBody(Transform parent)
    {
        if (parent == null)
        {
            return null;
        }

        if (parent.TryGetComponent(out Rigidbody directBody))
        {
            return directBody;
        }

        return parent.GetComponentInParent<Rigidbody>();
    }

    private static void IgnoreContainerSpawnImpacts(Transform parent)
    {
        if (parent == null)
        {
            return;
        }

        ImpactDamageReceiver impactReceiver = parent.GetComponent<ImpactDamageReceiver>();
        if (impactReceiver == null)
        {
            impactReceiver = parent.GetComponentInParent<ImpactDamageReceiver>();
        }

        impactReceiver?.IgnoreImpactsForSeconds(LoadImpactGracePeriod);
    }

    private static void ApplyTint(Renderer targetRenderer, Color color)
    {
        if (targetRenderer == null)
        {
            return;
        }

        Material material = targetRenderer.material;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }
}
