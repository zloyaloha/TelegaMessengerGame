using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class LevelManager : MonoBehaviour
{
    [Header("Level")]
    [SerializeField] private LevelConfig config;
    [SerializeField] private LevelConfig[] levelSequence;
    [SerializeField] private bool showMainMenuOnSceneLoad = true;
    [SerializeField] private bool autoStartOnSceneLoad = true;
    [SerializeField] private bool loopLevelSequence = true;

    [Header("References")]
    [SerializeField] private GameObject deliveryZonePrefab;
    [SerializeField] private GameObject cargoStackPrefab;
    [SerializeField] private CartController cartPrefab;
    [SerializeField] private WorldGenerator worldGenerator;
    [SerializeField] private PlayerMovement player;
    [SerializeField] private CartController cart;
    [SerializeField] private MinimapMarkers minimapMarkers;
    [SerializeField] private MainMenuUI mainMenuUI;
    [SerializeField] private LevelCompleteUI levelCompleteUI;

    [Header("Spawn Search")]
    [SerializeField, Min(1)] private int spawnSearchAttempts = 40;
    [SerializeField, Range(0.05f, 1f)] private float spawnSearchRadiusScale = 0.42f;
    [SerializeField, Range(0.05f, 1f)] private float fallbackSpawnRadiusScale = 0.5f;
    [SerializeField, Min(10f)] private float fallbackMapRadius = 120f;
    [SerializeField, Min(0.5f)] private float cartSpawnMinDistance = 3f;
    [SerializeField, Min(0.5f)] private float cartSpawnMaxDistance = 5f;
    [SerializeField, Min(0f)] private float cartGroundClearance = 0.08f;
    [SerializeField, Range(0f, 0.5f)] private float deliveryDistanceTolerance = 0.2f;
    [SerializeField, Min(1f)] private float deliveryProbeRadius = 20f;
    [SerializeField, Range(0.1f, 1f)] private float minSurfaceNormalY = 0.7f;
    [SerializeField, Min(0f)] private float waterClearance = 0.25f;
    [SerializeField, Min(10f)] private float spawnRayStartHeight = 600f;
    [SerializeField] private string[] blockedBiomeKeywords = { "mountain", "water" };

    [Header("Cargo Stack")]
    [SerializeField, Min(0f)] private float cargoGroundRowSpacing = 0.12f;
    [SerializeField, Min(0f)] private float cargoVerticalSpacing = 0.06f;
    [SerializeField, Min(0f)] private float cargoSecondRowDepthOffset = 0.12f;
    [SerializeField, Min(0f)] private float cargoPositionJitter = 0.05f;
    [SerializeField, Min(0f)] private float cargoRotationJitter = 5f;
    [SerializeField, Min(0f)] private float cargoSpawnImpactGracePeriod = 1.25f;

    [Header("Debug")]
    [SerializeField] private bool deactivateSceneCargoOnStart = true;
    [SerializeField] private bool verboseLogging = true;

    private readonly List<CargoInstance> _spawnedCargo = new();
    private readonly List<CargoInstance> _sceneCargoPool = new();
    private readonly HashSet<CargoInstance> _bootSceneCargo = new();
    private DeliveryZone _activeDeliveryZone;
    private GameObject _activeCargoDecoration;
    private int _currentLevelIndex;
    private bool _deliveryProcessed;

    private void Awake()
    {
        ResolveReferences();
        CaptureBootSceneCargo();
        player?.SetRandomSpawnOnStart(false);
        EnsureMainMenuUi();
        EnsureLevelCompleteUi();
    }

    private IEnumerator Start()
    {
        yield return null;

        if (showMainMenuOnSceneLoad && TryShowMainMenu())
        {
            yield break;
        }

        if (!autoStartOnSceneLoad)
        {
            yield break;
        }

        StartLevel();
    }

    private void OnDestroy()
    {
        if (mainMenuUI != null)
        {
            mainMenuUI.LevelSelected -= HandleMenuLevelSelected;
        }

        if (levelCompleteUI != null)
        {
            levelCompleteUI.NextLevelRequested -= HandleNextLevelRequested;
            levelCompleteUI.RestartRequested -= HandleRestartRequested;
        }
    }

    public void StartLevel()
    {
        ResolveReferences();
        EnsureMainMenuUi();
        EnsureLevelCompleteUi();

        if (!ResolveActiveConfig())
        {
            Debug.LogError("[LevelManager] LevelConfig is not assigned.");
            return;
        }

        if (player == null)
        {
            Debug.LogError("[LevelManager] PlayerMovement was not found.");
            return;
        }

        if (worldGenerator == null)
        {
            Debug.LogError("[LevelManager] WorldGenerator was not found.");
            return;
        }

        if (cart == null && cartPrefab != null)
        {
            cart = Instantiate(cartPrefab);
        }

        if (cart == null)
        {
            Debug.LogError("[LevelManager] CartController was not found and no cart prefab is assigned.");
            return;
        }

        Time.timeScale = 1f;
        levelCompleteUI.HideImmediate();
        _deliveryProcessed = false;

        CleanupPreviousRun();
        DetachCartFromPlayer();

        if (!TryResolveMapArea(out Vector3 mapCenter, out float spawnRadius))
        {
            Debug.LogError("[LevelManager] Failed to resolve map center and radius.");
            return;
        }

        if (!TryFindSpawnPoint(mapCenter, spawnRadius, out Vector3 spawnPoint))
        {
            Debug.LogError("[LevelManager] Failed to find a valid spawn point.");
            return;
        }

        if (!TryFindCartSpawnPoint(spawnPoint, out Vector3 cartPoint))
        {
            Debug.LogError("[LevelManager] Failed to find a valid cart spawn point.");
            return;
        }

        if (!TryFindDeliveryPoint(spawnPoint, out Vector3 deliveryPoint))
        {
            Debug.LogError("[LevelManager] Failed to find a valid delivery point.");
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(deliveryPoint - spawnPoint, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }

        Quaternion facingRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        PlacePlayer(spawnPoint, facingRotation);
        PlaceCart(cartPoint, facingRotation);
        SpawnCargoStack();
        SpawnDeliveryZone(deliveryPoint);

        minimapMarkers?.SetPlayerTarget(player.transform);
        minimapMarkers?.SetCartTarget(cart.transform);

        if (_activeDeliveryZone != null)
        {
            minimapMarkers?.SetGoal(_activeDeliveryZone.transform);
        }
        else
        {
            minimapMarkers?.SetGoal(deliveryPoint);
        }

        if (verboseLogging)
        {
            Debug.Log($"[LevelManager] Started '{config.levelName}' at {spawnPoint} with delivery at {deliveryPoint}.");
        }
    }

    public void StartLevel(LevelConfig selectedConfig)
    {
        if (selectedConfig != null)
        {
            config = selectedConfig;
            _currentLevelIndex = IndexOfConfig(selectedConfig);
        }

        StartLevel();
    }

    public void OnDelivery()
    {
        if (_deliveryProcessed || config == null || cart == null)
        {
            return;
        }

        _deliveryProcessed = true;
        List<CargoInstance> deliveredCargo = cart.Inventory != null
            ? cart.Inventory.GetLoadedCargos()
            : new List<CargoInstance>();

        LevelResult result = ScoringSystem.CalculateScore(
            config,
            _spawnedCargo.Count,
            deliveredCargo,
            cart.HpPercent);

        minimapMarkers?.ClearGoal();
        levelCompleteUI.SetNextLevelInteractable(CanAdvanceToNextLevel());
        levelCompleteUI.Show(result);

        if (verboseLogging)
        {
            Debug.Log($"[LevelManager] Delivery complete. Stars: {result.stars}, Delivered: {result.deliveredCargoCount}/{result.totalCargoCount}, Score: {result.finalScore:0.00}");
        }
    }

    private void HandleNextLevelRequested()
    {
        if (TryAdvanceToNextLevel())
        {
            StartLevel();
            return;
        }

        StartLevel();
    }

    private void HandleRestartRequested()
    {
        StartLevel();
    }

    private void HandleMenuLevelSelected(LevelConfig selectedConfig)
    {
        StartLevel(selectedConfig);
    }

    private void ResolveReferences()
    {
        if (worldGenerator == null)
        {
            worldGenerator = FindFirstObjectByType<WorldGenerator>();
        }

        if (player == null)
        {
            player = FindFirstObjectByType<PlayerMovement>();
        }

        if (cart == null)
        {
            cart = FindFirstObjectByType<CartController>();
        }

        if (minimapMarkers == null)
        {
            minimapMarkers = FindFirstObjectByType<MinimapMarkers>();
        }
    }

    private void EnsureLevelCompleteUi()
    {
        if (levelCompleteUI == null)
        {
            levelCompleteUI = FindFirstObjectByType<LevelCompleteUI>();
        }

        if (levelCompleteUI == null)
        {
            GameObject uiObject = new GameObject("LevelCompleteUI");
            levelCompleteUI = uiObject.AddComponent<LevelCompleteUI>();
        }

        levelCompleteUI.NextLevelRequested -= HandleNextLevelRequested;
        levelCompleteUI.RestartRequested -= HandleRestartRequested;
        levelCompleteUI.NextLevelRequested += HandleNextLevelRequested;
        levelCompleteUI.RestartRequested += HandleRestartRequested;
    }

    private void EnsureMainMenuUi()
    {
        if (mainMenuUI == null)
        {
            mainMenuUI = FindFirstObjectByType<MainMenuUI>();
        }

        if (mainMenuUI == null)
        {
            GameObject uiObject = new GameObject("MainMenuUI");
            mainMenuUI = uiObject.AddComponent<MainMenuUI>();
        }

        mainMenuUI.LevelSelected -= HandleMenuLevelSelected;
        mainMenuUI.LevelSelected += HandleMenuLevelSelected;
    }

    private bool TryShowMainMenu()
    {
        List<LevelConfig> menuLevels = GetMenuLevels();
        if (menuLevels.Count == 0)
        {
            return false;
        }

        EnsureMainMenuUi();
        mainMenuUI.Show(menuLevels);
        return true;
    }

    private void CaptureBootSceneCargo()
    {
        if (_bootSceneCargo.Count > 0)
        {
            return;
        }

        CargoInstance[] cargoInScene = FindObjectsByType<CargoInstance>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < cargoInScene.Length; i++)
        {
            if (cargoInScene[i] != null)
            {
                _bootSceneCargo.Add(cargoInScene[i]);
                _sceneCargoPool.Add(cargoInScene[i]);
            }
        }

        _sceneCargoPool.Sort(CompareSceneCargo);
    }

    private bool ResolveActiveConfig()
    {
        if (config != null)
        {
            _currentLevelIndex = IndexOfConfig(config);
            return true;
        }

        if (levelSequence != null && levelSequence.Length > 0)
        {
            _currentLevelIndex = Mathf.Clamp(_currentLevelIndex, 0, levelSequence.Length - 1);
            config = levelSequence[_currentLevelIndex];
            return config != null;
        }

        return false;
    }

    private void CleanupPreviousRun()
    {
        levelCompleteUI.HideImmediate();

        if (_activeDeliveryZone != null)
        {
            Destroy(_activeDeliveryZone.gameObject);
            _activeDeliveryZone = null;
        }

        DeliveryZone[] existingZones = FindObjectsByType<DeliveryZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < existingZones.Length; i++)
        {
            if (existingZones[i] != null)
            {
                Destroy(existingZones[i].gameObject);
            }
        }

        if (_activeCargoDecoration != null)
        {
            Destroy(_activeCargoDecoration);
            _activeCargoDecoration = null;
        }

        for (int i = 0; i < _spawnedCargo.Count; i++)
        {
            if (_spawnedCargo[i] != null)
            {
                if (!_bootSceneCargo.Contains(_spawnedCargo[i]))
                {
                    Destroy(_spawnedCargo[i].gameObject);
                }
            }
        }

        _spawnedCargo.Clear();

        for (int i = 0; i < _sceneCargoPool.Count; i++)
        {
            CargoInstance cargo = _sceneCargoPool[i];
            if (cargo == null)
            {
                continue;
            }

            ResetSceneCargoForPool(cargo, !deactivateSceneCargoOnStart);
        }
    }

    private void DetachCartFromPlayer()
    {
        if (player == null)
        {
            return;
        }

        CartPuller puller = player.GetComponent<CartPuller>();
        puller?.DetachCurrentCart();
    }

    private bool TryResolveMapArea(out Vector3 mapCenter, out float mapRadius)
    {
        mapCenter = Vector3.zero;
        mapRadius = Mathf.Max(10f, fallbackMapRadius);

        if (worldGenerator == null)
        {
            return false;
        }

        Rect bounds = worldGenerator.WorldBoundsXZ;
        if (bounds.width > Mathf.Epsilon && bounds.height > Mathf.Epsilon)
        {
            mapCenter = new Vector3(bounds.center.x, worldGenerator.transform.position.y, bounds.center.y);
            float halfMinExtent = Mathf.Min(bounds.width, bounds.height) * 0.5f;
            mapRadius = Mathf.Max(10f, halfMinExtent * Mathf.Clamp01(spawnSearchRadiusScale));
            return true;
        }

        mapCenter = worldGenerator.transform.position;
        return true;
    }

    private bool TryFindSpawnPoint(Vector3 center, float radius, out Vector3 spawnPoint)
    {
        bool found = SpawnPointValidator.TryGetValidPoint(
            center,
            radius,
            out spawnPoint,
            spawnSearchAttempts,
            minSurfaceNormalY,
            waterClearance,
            spawnRayStartHeight,
            blockedBiomeKeywords);

        if (found)
        {
            return true;
        }

        float fallbackRadius = Mathf.Max(
            Mathf.Max(10f, fallbackMapRadius),
            radius / Mathf.Max(0.05f, Mathf.Clamp01(spawnSearchRadiusScale)) * Mathf.Clamp01(fallbackSpawnRadiusScale));

        return SpawnPointValidator.TryGetValidPoint(
            center,
            fallbackRadius,
            out spawnPoint,
            spawnSearchAttempts * 2,
            minSurfaceNormalY,
            waterClearance,
            spawnRayStartHeight,
            blockedBiomeKeywords);
    }

    private bool TryFindCartSpawnPoint(Vector3 playerSpawnPoint, out Vector3 cartSpawnPoint)
    {
        cartSpawnPoint = Vector3.zero;
        int attempts = Mathf.Max(8, spawnSearchAttempts);
        float minDistance = Mathf.Max(0.5f, cartSpawnMinDistance);
        float maxDistance = Mathf.Max(minDistance, cartSpawnMaxDistance);
        float probeRadius = Mathf.Max(1.5f, (maxDistance - minDistance) * 0.75f);

        for (int i = 0; i < attempts; i++)
        {
            Vector2 direction2D = Random.insideUnitCircle.normalized;
            if (direction2D.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            float distance = Random.Range(minDistance, maxDistance);
            Vector3 candidateCenter = playerSpawnPoint + new Vector3(direction2D.x, 0f, direction2D.y) * distance;
            if (!SpawnPointValidator.TryGetValidPoint(
                    candidateCenter,
                    probeRadius,
                    out Vector3 validatedPoint,
                    8,
                    minSurfaceNormalY,
                    waterClearance,
                    spawnRayStartHeight,
                    blockedBiomeKeywords))
            {
                continue;
            }

            float resolvedDistance = Vector3.Distance(
                new Vector3(playerSpawnPoint.x, 0f, playerSpawnPoint.z),
                new Vector3(validatedPoint.x, 0f, validatedPoint.z));

            if (resolvedDistance < minDistance || resolvedDistance > maxDistance + probeRadius)
            {
                continue;
            }

            cartSpawnPoint = validatedPoint;
            return true;
        }

        return false;
    }

    private bool TryFindDeliveryPoint(Vector3 spawnPoint, out Vector3 deliveryPoint)
    {
        deliveryPoint = Vector3.zero;

        float targetDistance = Mathf.Max(5f, config.deliveryDistance);
        float minDistance = targetDistance * (1f - Mathf.Clamp01(deliveryDistanceTolerance));
        float maxDistance = targetDistance * (1f + Mathf.Clamp01(deliveryDistanceTolerance));
        int attempts = Mathf.Max(10, spawnSearchAttempts * 2);

        for (int i = 0; i < attempts; i++)
        {
            Vector2 direction2D = Random.insideUnitCircle.normalized;
            if (direction2D.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            float distance = Random.Range(minDistance, maxDistance);
            Vector3 candidateCenter = spawnPoint + new Vector3(direction2D.x, 0f, direction2D.y) * distance;
            if (!SpawnPointValidator.TryGetValidPoint(
                    candidateCenter,
                    deliveryProbeRadius,
                    out Vector3 validatedPoint,
                    10,
                    minSurfaceNormalY,
                    waterClearance,
                    spawnRayStartHeight,
                    blockedBiomeKeywords))
            {
                continue;
            }

            float resolvedDistance = Vector3.Distance(
                new Vector3(spawnPoint.x, 0f, spawnPoint.z),
                new Vector3(validatedPoint.x, 0f, validatedPoint.z));

            if (resolvedDistance < minDistance || resolvedDistance > maxDistance)
            {
                continue;
            }

            deliveryPoint = validatedPoint;
            return true;
        }

        return false;
    }

    private void PlacePlayer(Vector3 spawnPoint, Quaternion rotation)
    {
        CharacterController characterController = player.GetComponent<CharacterController>();
        float clearance = characterController != null
            ? Mathf.Clamp(characterController.skinWidth, 0.02f, 0.05f)
            : 0.02f;

        player.TeleportToGround(spawnPoint, rotation, clearance);
    }

    private void PlaceCart(Vector3 cartPoint, Quaternion rotation)
    {
        Vector3 cartPosition = cartPoint + Vector3.up * ResolveCartGroundOffset(rotation);
        cart.transform.SetPositionAndRotation(cartPosition, rotation);

        if (cart.CartRigidbody != null)
        {
            cart.CartRigidbody.linearVelocity = Vector3.zero;
            cart.CartRigidbody.angularVelocity = Vector3.zero;
            cart.CartRigidbody.position = cartPosition;
            cart.CartRigidbody.rotation = rotation;
        }

        cart.Durability?.ResetDurability();
        Physics.SyncTransforms();
    }

    private float ResolveCartGroundOffset(Quaternion rotation)
    {
        if (cart == null)
        {
            return Mathf.Max(0f, cartGroundClearance);
        }

        Collider[] colliders = cart.GetComponentsInChildren<Collider>(true);
        Renderer[] renderers = cart.GetComponentsInChildren<Renderer>(true);
        if (!CargoGridPlacementUtility.TryGetLocalBounds(cart.transform, colliders, renderers, out Bounds localBounds))
        {
            return Mathf.Max(0f, cartGroundClearance);
        }

        Bounds rotatedBounds = CargoGridPlacementUtility.TransformBounds(localBounds, Matrix4x4.Rotate(rotation));
        return Mathf.Max(0f, -rotatedBounds.min.y) + Mathf.Max(0f, cartGroundClearance);
    }

    private void SpawnCargoStack()
    {
        int requestedCount = config != null ? Mathf.Max(1, config.cargoCount) : 0;
        List<CargoInstance> selectedCargo = SelectSceneCargo(requestedCount);
        if (selectedCargo.Count == 0)
        {
            Debug.LogError("[LevelManager] No scene cargo is available for the level.");
            return;
        }

        if (verboseLogging && selectedCargo.Count < requestedCount)
        {
            Debug.LogWarning($"[LevelManager] Requested {requestedCount} cargo items, but only found {selectedCargo.Count} scene boxes. Using available scene cargo only.");
        }

        ResolveCargoStackLayout(out Vector3 stackCenter, out Vector3 stackRight, out Vector3 stackForward);
        Vector3 stackGroundPoint = ResolveCargoStackGroundPoint(stackCenter);
        if (cargoStackPrefab != null)
        {
            Quaternion decorationRotation = Quaternion.LookRotation(stackForward, Vector3.up);
            _activeCargoDecoration = Instantiate(cargoStackPrefab, stackGroundPoint, decorationRotation);
        }

        int groundRowCount = Mathf.CeilToInt(selectedCargo.Count * 0.5f);
        groundRowCount = Mathf.Max(1, groundRowCount);

        for (int i = 0; i < selectedCargo.Count; i++)
        {
            CargoInstance cargoInstance = selectedCargo[i];
            if (cargoInstance == null)
            {
                continue;
            }

            GameObject cargoObject = cargoInstance.gameObject;
            cargoObject.SetActive(true);

            int rowIndex = i < groundRowCount ? i : i - groundRowCount;
            int layerIndex = i < groundRowCount ? 0 : 1;
            int itemsInLayer = layerIndex == 0 ? groundRowCount : selectedCargo.Count - groundRowCount;
            float horizontalStep = ResolveCargoHorizontalStep(cargoInstance, cargoInstance.Data);
            float verticalStep = ResolveCargoHeight(cargoInstance) + cargoVerticalSpacing;

            float rowOffset = (rowIndex - ((itemsInLayer - 1) * 0.5f)) * (horizontalStep + cargoGroundRowSpacing);
            Vector3 baseOffset = (stackRight * rowOffset)
                + (stackForward * (layerIndex * cargoSecondRowDepthOffset));

            Vector3 baseSamplePoint = stackCenter + baseOffset;
            bool usedGroundFallback = !TryProjectToGround(baseSamplePoint, out Vector3 groundPoint);
            if (usedGroundFallback)
            {
                Vector3 fallbackSamplePoint = stackGroundPoint + new Vector3(baseOffset.x, 0f, baseOffset.z);
                if (!TryProjectToGround(fallbackSamplePoint, out groundPoint))
                {
                    groundPoint = fallbackSamplePoint;
                }

                if (verboseLogging)
                {
                    Debug.LogWarning($"[LevelManager] Failed to project cargo slot for '{cargoInstance.name}' near {baseSamplePoint}. Using fallback near cart instead.");
                }
            }

            float jitterX = Random.Range(-cargoPositionJitter, cargoPositionJitter);
            float jitterZ = Random.Range(-cargoPositionJitter, cargoPositionJitter);
            float jitterYaw = Random.Range(-cargoRotationJitter, cargoRotationJitter);

            Vector3 position = groundPoint
                + (Vector3.up * (ResolveCargoHalfHeight(cargoInstance) + (layerIndex * verticalStep)))
                + (stackRight * jitterX)
                + (stackForward * jitterZ);

            Quaternion baseRotation = Quaternion.LookRotation(stackForward, Vector3.up);
            Quaternion cargoRotation = Quaternion.Euler(0f, baseRotation.eulerAngles.y + jitterYaw, 0f);
            cargoInstance.ResetForLevel(position, cargoRotation);
            ImpactDamageReceiver impactReceiver = cargoObject.GetComponent<ImpactDamageReceiver>();
            impactReceiver?.IgnoreImpactsForSeconds(cargoSpawnImpactGracePeriod);

            _spawnedCargo.Add(cargoInstance);
        }

        if (verboseLogging)
        {
            Debug.Log($"[LevelManager] Spawned {_spawnedCargo.Count} scene cargo items near the cart.");
        }
    }

    private void SpawnDeliveryZone(Vector3 deliveryPoint)
    {
        if (deliveryZonePrefab == null)
        {
            Debug.LogError("[LevelManager] Delivery zone prefab is not assigned.");
            return;
        }

        GameObject zoneObject = Instantiate(deliveryZonePrefab, deliveryPoint, Quaternion.identity);
        _activeDeliveryZone = zoneObject.GetComponent<DeliveryZone>();
        if (_activeDeliveryZone == null)
        {
            _activeDeliveryZone = zoneObject.AddComponent<DeliveryZone>();
        }

        _activeDeliveryZone.Initialize(this, config.deliveryRadius);
    }

    private void ResolveCargoStackLayout(out Vector3 stackCenter, out Vector3 stackRight, out Vector3 stackForward)
    {
        Bounds cartBounds = CalculateWorldBounds(cart.gameObject);
        Vector3 preferredForward = Vector3.ProjectOnPlane(player.transform.position - cart.transform.position, Vector3.up);
        if (preferredForward.sqrMagnitude < 0.001f)
        {
            preferredForward = -cart.transform.right;
        }

        stackForward = preferredForward.normalized;
        stackRight = Vector3.Cross(Vector3.up, stackForward).normalized;
        if (stackRight.sqrMagnitude < 0.001f)
        {
            stackRight = cart.transform.right.sqrMagnitude > 0.001f ? cart.transform.right.normalized : Vector3.right;
        }

        float forwardOffset = Mathf.Max(1.2f, cartBounds.extents.z + 1f);
        stackCenter = cart.transform.position + (stackForward * forwardOffset);
    }

    private Vector3 ResolveCargoStackGroundPoint(Vector3 stackCenter)
    {
        if (TryProjectToGround(stackCenter, out Vector3 stackGroundPoint))
        {
            return stackGroundPoint;
        }

        if (TryProjectToGround(cart.transform.position, out Vector3 cartGroundPoint))
        {
            if (verboseLogging)
            {
                Debug.LogWarning("[LevelManager] Failed to project the cargo stack center to the ground. Using the cart ground point instead.");
            }

            return cartGroundPoint;
        }

        if (verboseLogging)
        {
            Debug.LogWarning("[LevelManager] Failed to project cargo stack near the cart. Falling back to the cart transform position.");
        }

        return cart.transform.position;
    }

    private Bounds CalculateWorldBounds(GameObject root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        Bounds bounds = new Bounds(root.transform.position, Vector3.one);
        bool initialized = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger)
            {
                continue;
            }

            if (!initialized)
            {
                bounds = collider.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return bounds;
    }

    private float ResolveCargoHorizontalStep(CargoInstance cargoInstance, CargoData cargoData)
    {
        float colliderWidth = 0.6f;
        Collider cargoCollider = cargoInstance.GetComponentInChildren<Collider>();
        if (cargoCollider != null)
        {
            colliderWidth = Mathf.Max(cargoCollider.bounds.size.x, cargoCollider.bounds.size.z);
        }

        int gridCells = cargoData != null ? Mathf.Max(cargoData.GridSize.x, cargoData.GridSize.z) : 1;
        return Mathf.Max(colliderWidth, 0.55f * gridCells);
    }

    private float ResolveCargoHeight(CargoInstance cargoInstance)
    {
        Collider cargoCollider = cargoInstance.GetComponentInChildren<Collider>();
        return cargoCollider != null ? Mathf.Max(0.4f, cargoCollider.bounds.size.y) : 0.5f;
    }

    private float ResolveCargoHalfHeight(CargoInstance cargoInstance)
    {
        return ResolveCargoHeight(cargoInstance) * 0.5f;
    }

    private List<CargoInstance> SelectSceneCargo(int requestedCount)
    {
        List<CargoInstance> selectedCargo = new List<CargoInstance>();
        int safeRequestedCount = Mathf.Max(1, requestedCount);

        for (int i = 0; i < _sceneCargoPool.Count && selectedCargo.Count < safeRequestedCount; i++)
        {
            CargoInstance cargo = _sceneCargoPool[i];
            if (cargo == null)
            {
                continue;
            }

            selectedCargo.Add(cargo);
        }

        return selectedCargo;
    }

    private void ResetSceneCargoForPool(CargoInstance cargo, bool keepActive)
    {
        if (cargo == null)
        {
            return;
        }

        cargo.ResetForLevel(cargo.transform.position, cargo.transform.rotation);

        if (!keepActive)
        {
            cargo.gameObject.SetActive(false);
        }
    }

    private static int CompareSceneCargo(CargoInstance left, CargoInstance right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        string leftName = !string.IsNullOrWhiteSpace(left.CargoName) ? left.CargoName : left.name;
        string rightName = !string.IsNullOrWhiteSpace(right.CargoName) ? right.CargoName : right.name;
        return string.Compare(leftName, rightName, System.StringComparison.Ordinal);
    }

    private List<LevelConfig> GetMenuLevels()
    {
        List<LevelConfig> menuLevels = new List<LevelConfig>();

        if (levelSequence != null && levelSequence.Length > 0)
        {
            for (int i = 0; i < levelSequence.Length; i++)
            {
                LevelConfig levelConfig = levelSequence[i];
                if (levelConfig != null && !menuLevels.Contains(levelConfig))
                {
                    menuLevels.Add(levelConfig);
                }
            }
        }

        if (menuLevels.Count == 0 && config != null)
        {
            menuLevels.Add(config);
        }

        return menuLevels;
    }

    private bool TryProjectToGround(Vector3 worldPoint, out Vector3 groundPoint)
    {
        if (SpawnPointValidator.TryProjectToTerrain(worldPoint, out groundPoint, spawnRayStartHeight))
        {
            return true;
        }

        return SpawnPointValidator.TryGetValidPoint(
            worldPoint,
            4f,
            out groundPoint,
            6,
            minSurfaceNormalY,
            waterClearance,
            spawnRayStartHeight,
            blockedBiomeKeywords);
    }

    private int IndexOfConfig(LevelConfig levelConfig)
    {
        if (levelSequence == null || levelConfig == null)
        {
            return 0;
        }

        for (int i = 0; i < levelSequence.Length; i++)
        {
            if (levelSequence[i] == levelConfig)
            {
                return i;
            }
        }

        return 0;
    }

    private bool CanAdvanceToNextLevel()
    {
        if (levelSequence == null || levelSequence.Length <= 1)
        {
            return false;
        }

        return loopLevelSequence || _currentLevelIndex < levelSequence.Length - 1;
    }

    private bool TryAdvanceToNextLevel()
    {
        if (levelSequence == null || levelSequence.Length == 0)
        {
            return false;
        }

        int nextIndex = _currentLevelIndex + 1;
        if (nextIndex >= levelSequence.Length)
        {
            if (!loopLevelSequence)
            {
                return false;
            }

            nextIndex = 0;
        }

        if (nextIndex < 0 || nextIndex >= levelSequence.Length || levelSequence[nextIndex] == null)
        {
            return false;
        }

        _currentLevelIndex = nextIndex;
        config = levelSequence[_currentLevelIndex];
        return true;
    }
}
