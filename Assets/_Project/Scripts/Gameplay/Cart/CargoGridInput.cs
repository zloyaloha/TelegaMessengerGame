using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class CargoGridInput : MonoBehaviour
{
    private struct PlayerControlState
    {
        public bool movementEnabled;
        public bool interactorEnabled;
        public bool carryEnabled;
        public bool cartPullerEnabled;
        public bool cameraControllerEnabled;
    }

    [Header("References")]
    [SerializeField] private CartInventory cartInventory;
    [SerializeField] private CargoGridVisualizer cargoGridVisualizer;
    [SerializeField] private CargoGridCamera cargoGridCamera;
    [SerializeField] private CartController cartController;
    [SerializeField] private CartCargoZone cargoZone;
    [SerializeField] private Collider openZoneCollider;

    [Header("Input")]
    [SerializeField] private LayerMask gridRaycastMask = default;
    [SerializeField, Min(1f)] private float maxRayDistance = 100f;
    [SerializeField, Min(0f)] private float dragThresholdPixels = 8f;
    [SerializeField, Min(0f)] private float openZoneGraceDistance = 0.75f;
    [SerializeField, Min(0f)] private float playerZoneCheckHeight = 1f;

    private readonly HashSet<PlayerInteractor> _interactorsInZone = new HashSet<PlayerInteractor>();
    private readonly RaycastHit[] _rayHits = new RaycastHit[32];

    private Keyboard _keyboard;
    private Mouse _mouse;

    private PlayerMovement _activePlayerMovement;
    private PlayerInteractor _activePlayerInteractor;
    private PlayerCarryController _activeCarryController;
    private CartPuller _activeCartPuller;
    private CameraController _activeCameraController;
    private PlayerControlState _cachedControlState;

    private CursorLockMode _previousCursorLockMode;
    private bool _previousCursorVisible;
    private bool _restorePlayerControlsOnCameraClose;
    private bool _dropCarriedCargoAfterClose;

    private bool _isOpen;
    private Vector3Int? _hoveredCell;

    private CargoInstance _pressedCargo;
    private Vector3Int _pressedOrigin;
    private Vector2 _pressedMousePosition;

    private bool _isDraggingCargo;
    private CargoInstance _draggedCargo;
    private Vector3Int _draggedOrigin;

    public bool IsOpen => _isOpen;

    private void Awake()
    {
        _keyboard = Keyboard.current;
        _mouse = Mouse.current;

        if (cartInventory == null)
        {
            cartInventory = GetComponent<CartInventory>();
        }

        if (cargoGridVisualizer == null)
        {
            cargoGridVisualizer = GetComponentInChildren<CargoGridVisualizer>(true);
        }

        if (cargoGridCamera == null)
        {
            cargoGridCamera = GetComponentInChildren<CargoGridCamera>(true);
        }

        if (cartController == null)
        {
            cartController = GetComponent<CartController>();
        }

        if (cargoZone == null)
        {
            cargoZone = GetComponentInChildren<CartCargoZone>(true);
        }

        if (openZoneCollider == null && cargoZone != null)
        {
            openZoneCollider = cargoZone.GetComponent<Collider>();
        }

        if (cargoGridCamera != null)
        {
            cargoGridCamera.Closed += HandleGridCameraClosed;
        }
    }

    private void OnDestroy()
    {
        if (cargoGridCamera != null)
        {
            cargoGridCamera.Closed -= HandleGridCameraClosed;
        }
    }

    private void Update()
    {
        if (!_isOpen)
        {
            return;
        }

        if (_keyboard == null)
        {
            _keyboard = Keyboard.current;
        }

        if (_mouse == null)
        {
            _mouse = Mouse.current;
        }

        if (_keyboard == null || _mouse == null)
        {
            return;
        }

        if (_keyboard.gKey.wasPressedThisFrame)
        {
            _dropCarriedCargoAfterClose = true;
            CloseGrid();
            return;
        }

        if (_keyboard.escapeKey.wasPressedThisFrame
            || _mouse.rightButton.wasPressedThisFrame)
        {
            CloseGrid();
            return;
        }

        UpdateHoveredCell();
        UpdateInteractionState();
        RefreshPreviewVisuals();
    }

    private void OnDisable()
    {
        if (_isOpen)
        {
            ForceCloseImmediately();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerInteractor interactor = other.GetComponentInParent<PlayerInteractor>();
        if (interactor != null)
        {
            _interactorsInZone.Add(interactor);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerInteractor interactor = other.GetComponentInParent<PlayerInteractor>();
        if (interactor != null)
        {
            _interactorsInZone.Remove(interactor);
        }
    }

    public bool CanOpenFor(PlayerInteractor interactor)
    {
        return interactor != null
            && !_isOpen
            && HasRequiredReferences()
            && interactor.CarryController != null
            && interactor.CarryController.IsCarrying
            && IsWithinOpenZone(interactor);
    }

    public bool TryOpenFromInteraction(PlayerInteractor interactor)
    {
        if (!CanOpenFor(interactor))
        {
            return false;
        }

        CachePlayerReferences(interactor);
        return OpenGrid();
    }

    private bool OpenGrid()
    {
        if (_isOpen || !HasRequiredReferences() || _activeCarryController == null || !_activeCarryController.IsCarrying)
        {
            return false;
        }

        CargoInstance carriedCargo = _activeCarryController.CarriedItem;
        if (carriedCargo == null)
        {
            return false;
        }

        _isOpen = true;
        _restorePlayerControlsOnCameraClose = false;
        _dropCarriedCargoAfterClose = false;
        _hoveredCell = null;
        _pressedCargo = null;
        _draggedCargo = null;
        _isDraggingCargo = false;

        carriedCargo.SetPresentationHidden(true);
        cartInventory?.SyncGridFromPhysics();

        CacheCurrentControlState();
        SetPlayerControlsBlocked(true);

        _previousCursorLockMode = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        cargoGridVisualizer?.Show();

        Camera sourceCamera = Camera.main;
        if (sourceCamera == null && _activeCameraController != null)
        {
            sourceCamera = _activeCameraController.GetComponentInChildren<Camera>();
        }

        cargoGridCamera?.Open(sourceCamera, _activeCameraController);
        RefreshPreviewVisuals();
        return true;
    }

    private void CloseGrid()
    {
        if (!_isOpen)
        {
            return;
        }

        if (_isDraggingCargo && _draggedCargo != null && cartInventory != null)
        {
            cartInventory.Place(_draggedOrigin, _draggedCargo);
            _draggedCargo.SetPresentationHidden(false);
            _draggedCargo = null;
            _isDraggingCargo = false;
        }

        _pressedCargo = null;
        _hoveredCell = null;
        cargoGridVisualizer?.Hide();

        CargoInstance carriedCargo = _activeCarryController != null ? _activeCarryController.CarriedItem : null;
        if (carriedCargo != null)
        {
            carriedCargo.SetPresentationHidden(false);
        }

        _restorePlayerControlsOnCameraClose = true;
        cargoGridCamera?.Close();

        if (cargoGridCamera == null)
        {
            HandleGridCameraClosed();
        }
    }

    private void ForceCloseImmediately()
    {
        bool shouldRestore = _isOpen || _restorePlayerControlsOnCameraClose;

        if (_isDraggingCargo && _draggedCargo != null && cartInventory != null)
        {
            cartInventory.Place(_draggedOrigin, _draggedCargo);
        }

        _isOpen = false;
        _restorePlayerControlsOnCameraClose = false;
        _dropCarriedCargoAfterClose = false;
        _pressedCargo = null;
        _hoveredCell = null;
        _isDraggingCargo = false;
        _draggedCargo = null;
        cargoGridVisualizer?.Hide();
        cargoGridCamera?.ForceRestore();
        RestoreActiveCargoVisuals();

        if (!shouldRestore)
        {
            return;
        }

        RestoreCursorState();
        SetPlayerControlsBlocked(false);
    }

    private void UpdateHoveredCell()
    {
        _hoveredCell = null;

        Camera activeCamera = cargoGridCamera != null ? cargoGridCamera.ActiveCamera : null;
        if (activeCamera == null || _mouse == null)
        {
            return;
        }

        Ray ray = activeCamera.ScreenPointToRay(_mouse.position.ReadValue());
        int layerMask = gridRaycastMask.value == 0 ? Physics.AllLayers : gridRaycastMask.value;
        int hitCount = Physics.RaycastNonAlloc(ray, _rayHits, maxRayDistance, layerMask, QueryTriggerInteraction.Collide);

        float bestDistance = float.MaxValue;
        CargoGridCellView bestCellView = null;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _rayHits[i];
            if (hit.collider == null)
            {
                continue;
            }

            CargoGridCellView cellView = hit.collider.GetComponent<CargoGridCellView>();
            if (cellView == null || hit.distance >= bestDistance)
            {
                continue;
            }

            bestDistance = hit.distance;
            bestCellView = cellView;
        }

        if (bestCellView != null)
        {
            _hoveredCell = bestCellView.Coords;
        }
    }

    private void UpdateInteractionState()
    {
        if (_mouse == null)
        {
            return;
        }

        if (_isDraggingCargo)
        {
            if (_mouse.leftButton.wasReleasedThisFrame)
            {
                FinalizeDraggedCargo();
            }

            return;
        }

        if (_pressedCargo != null && _mouse.leftButton.isPressed)
        {
            float dragDistance = Vector2.Distance(_pressedMousePosition, _mouse.position.ReadValue());
            if (dragDistance >= dragThresholdPixels)
            {
                BeginDragFromPressedCargo();
                return;
            }
        }

        if (_mouse.leftButton.wasPressedThisFrame)
        {
            if (TryPlaceCarriedCargo())
            {
                return;
            }

            CapturePressedCargo();
        }

        if (_mouse.leftButton.wasReleasedThisFrame && _pressedCargo != null)
        {
            PickUpPressedCargo();
            _pressedCargo = null;
        }
    }

    private void RefreshPreviewVisuals()
    {
        if (cargoGridVisualizer == null)
        {
            return;
        }

        cargoGridVisualizer.SetHoveredCell(_hoveredCell);
        cargoGridVisualizer.ClearPlacementPreview();

        CargoInstance previewCargo = null;
        if (_isDraggingCargo)
        {
            previewCargo = _draggedCargo;
        }
        else if (_activeCarryController != null && _activeCarryController.IsCarrying)
        {
            previewCargo = _activeCarryController.CarriedItem;
        }

        if (previewCargo == null || !_hoveredCell.HasValue || cartInventory == null)
        {
            cargoGridVisualizer.HideGhost();
            return;
        }

        bool validPlacement = cartInventory.CanPlace(_hoveredCell.Value, previewCargo.GridSize);
        cargoGridVisualizer.SetPlacementPreview(_hoveredCell.Value, previewCargo.GridSize, validPlacement);
        cargoGridVisualizer.UpdateGhost(previewCargo, _hoveredCell.Value, validPlacement);
    }

    private bool TryPlaceCarriedCargo()
    {
        if (_activeCarryController == null || !_activeCarryController.IsCarrying || !_hoveredCell.HasValue || cartInventory == null)
        {
            return false;
        }

        CargoInstance carriedCargo = _activeCarryController.CarriedItem;
        if (carriedCargo == null || !cartInventory.CanPlace(_hoveredCell.Value, carriedCargo.GridSize))
        {
            return false;
        }

        cartInventory.Place(_hoveredCell.Value, carriedCargo);
        _activeCarryController.NotifyCarriedItemUnavailable(carriedCargo);
        carriedCargo.SetPresentationHidden(false);
        return true;
    }

    private void CapturePressedCargo()
    {
        _pressedCargo = null;

        if (_hoveredCell.HasValue && cartInventory != null
            && cartInventory.TryGetPlacement(_hoveredCell.Value, out CargoInstance cargo, out Vector3Int origin, out _))
        {
            _pressedCargo = cargo;
            _pressedOrigin = origin;
            _pressedMousePosition = _mouse != null ? _mouse.position.ReadValue() : Vector2.zero;
        }
    }

    private void PickUpPressedCargo()
    {
        if (_pressedCargo == null || cartInventory == null || _activeCarryController == null)
        {
            return;
        }

        CargoInstance removedCargo = cartInventory.Remove(_pressedOrigin);
        if (removedCargo == null)
        {
            return;
        }

        removedCargo.UnloadFromCart();
        if (!_activeCarryController.TryPickUp(removedCargo))
        {
            cartInventory.Place(_pressedOrigin, removedCargo);
            return;
        }

        removedCargo.SetPresentationHidden(true);
    }

    private void BeginDragFromPressedCargo()
    {
        if (_pressedCargo == null || cartInventory == null)
        {
            return;
        }

        CargoInstance removedCargo = cartInventory.Remove(_pressedOrigin, true);
        if (removedCargo == null)
        {
            _pressedCargo = null;
            return;
        }

        _draggedCargo = removedCargo;
        _draggedOrigin = _pressedOrigin;
        _isDraggingCargo = true;
        _pressedCargo.SetPresentationHidden(true);
        _pressedCargo = null;
    }

    private void FinalizeDraggedCargo()
    {
        if (_draggedCargo == null || cartInventory == null)
        {
            _isDraggingCargo = false;
            return;
        }

        Vector3Int targetPosition = _hoveredCell ?? _draggedOrigin;
        bool canPlace = _hoveredCell.HasValue && cartInventory.CanPlace(targetPosition, _draggedCargo.GridSize);
        cartInventory.Place(canPlace ? targetPosition : _draggedOrigin, _draggedCargo);
        _draggedCargo.SetPresentationHidden(false);
        _draggedCargo = null;
        _isDraggingCargo = false;
    }

    private void CachePlayerReferences(PlayerInteractor interactor)
    {
        _activePlayerInteractor = interactor;
        _activeCarryController = interactor != null ? interactor.CarryController : null;
        _activePlayerMovement = interactor != null ? interactor.GetComponent<PlayerMovement>() : null;
        _activeCartPuller = interactor != null ? interactor.GetComponent<CartPuller>() : null;
        _activeCameraController = FindFirstObjectByType<CameraController>();
    }

    private void CacheCurrentControlState()
    {
        _cachedControlState = new PlayerControlState
        {
            movementEnabled = _activePlayerMovement != null && _activePlayerMovement.enabled,
            interactorEnabled = _activePlayerInteractor != null && _activePlayerInteractor.enabled,
            carryEnabled = _activeCarryController != null && _activeCarryController.enabled,
            cartPullerEnabled = _activeCartPuller != null && _activeCartPuller.enabled,
            cameraControllerEnabled = _activeCameraController != null && _activeCameraController.enabled
        };
    }

    private void SetPlayerControlsBlocked(bool blocked)
    {
        if (_activePlayerMovement != null)
        {
            _activePlayerMovement.enabled = blocked ? false : _cachedControlState.movementEnabled;
        }

        if (_activePlayerInteractor != null)
        {
            _activePlayerInteractor.enabled = blocked ? false : _cachedControlState.interactorEnabled;
        }

        if (_activeCarryController != null)
        {
            _activeCarryController.enabled = blocked ? false : _cachedControlState.carryEnabled;
        }

        if (_activeCartPuller != null)
        {
            _activeCartPuller.enabled = blocked ? false : _cachedControlState.cartPullerEnabled;
        }

        if (_activeCameraController != null && cargoGridCamera == null)
        {
            _activeCameraController.enabled = blocked ? false : _cachedControlState.cameraControllerEnabled;
        }
    }

    private void RestoreCursorState()
    {
        Cursor.lockState = _previousCursorLockMode;
        Cursor.visible = _previousCursorVisible;
    }

    private void HandleGridCameraClosed()
    {
        if (!_restorePlayerControlsOnCameraClose)
        {
            return;
        }

        _isOpen = false;
        _restorePlayerControlsOnCameraClose = false;
        RestoreCursorState();
        SetPlayerControlsBlocked(false);

        bool shouldDropCarriedCargo = _dropCarriedCargoAfterClose;
        _dropCarriedCargoAfterClose = false;

        if (shouldDropCarriedCargo && _activeCarryController != null)
        {
            _activeCarryController.DropCarriedItem();
        }
    }

    private bool HasRequiredReferences()
    {
        return cartInventory != null
            && cargoGridVisualizer != null
            && cargoGridCamera != null;
    }

    private void RestoreActiveCargoVisuals()
    {
        CargoInstance carriedCargo = _activeCarryController != null ? _activeCarryController.CarriedItem : null;
        if (carriedCargo != null)
        {
            carriedCargo.SetPresentationHidden(false);
        }
    }

    private bool IsWithinOpenZone(PlayerInteractor interactor)
    {
        if (interactor == null)
        {
            return false;
        }

        if (_interactorsInZone.Contains(interactor))
        {
            return true;
        }

        if (openZoneCollider == null)
        {
            return true;
        }

        Vector3 playerCheckPoint = interactor.transform.position + (Vector3.up * playerZoneCheckHeight);
        if (IsPointNearOpenZone(playerCheckPoint))
        {
            return true;
        }

        PlayerCarryController carryController = interactor.CarryController;
        CargoInstance carriedCargo = carryController != null ? carryController.CarriedItem : null;
        if (carriedCargo == null)
        {
            return false;
        }

        if (IsPointNearOpenZone(carriedCargo.transform.position))
        {
            return true;
        }

        Rigidbody carriedBody = carriedCargo.ItemRigidbody;
        return carriedBody != null && IsPointNearOpenZone(carriedBody.worldCenterOfMass);
    }

    private bool IsPointNearOpenZone(Vector3 worldPoint)
    {
        if (openZoneCollider == null)
        {
            return true;
        }

        Vector3 closestPoint = openZoneCollider.ClosestPoint(worldPoint);
        return (closestPoint - worldPoint).sqrMagnitude <= openZoneGraceDistance * openZoneGraceDistance;
    }
}
