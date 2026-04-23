using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CartInventory : MonoBehaviour
{
    [Serializable]
    private struct CargoPlacement
    {
        public Vector3Int position;
        public Vector3Int size;

        public CargoPlacement(Vector3Int position, Vector3Int size)
        {
            this.position = position;
            this.size = size;
        }
    }

    [Header("Grid")]
    [SerializeField] private Vector3Int gridDimensions = new Vector3Int(2, 1, 2);
    [SerializeField, Min(0.05f)] private float cellWorldSize = 0.5f;
    [SerializeField] private Vector3 gridOriginLocalOffset = Vector3.zero;
    [SerializeField] private bool deriveOriginFromCargoZone = true;
    [SerializeField] private Transform cargoParent;
    [SerializeField] private CartCargoZone cargoZone;
    [SerializeField] private CartColliderSetup colliderSetup;

    [Header("Placement")]
    [SerializeField] private Vector3 cargoPlacementClearance = new Vector3(0.06f, 0.02f, 0.06f);

    private readonly Dictionary<CargoInstance, CargoPlacement> _placements = new Dictionary<CargoInstance, CargoPlacement>();
    private readonly HashSet<CargoInstance> _loadedCargos = new HashSet<CargoInstance>();
    private GridCell[,,] _cells;

    public event Action InventoryChanged;
    public event Action<CargoInstance> OnCargoLost;

    public Vector3Int GridDimensions => new Vector3Int(
        Mathf.Max(1, gridDimensions.x),
        Mathf.Max(1, gridDimensions.y),
        Mathf.Max(1, gridDimensions.z));

    public float CellWorldSize => Mathf.Max(0.05f, cellWorldSize);
    public Transform CargoParent => cargoParent != null ? cargoParent : transform;
    public GridCell[,,] Cells => _cells;
    public CartCargoZone CargoZone => cargoZone;
    public CartColliderSetup ColliderSetup => colliderSetup;

    private void Awake()
    {
        if (cargoZone == null)
        {
            cargoZone = GetComponentInChildren<CartCargoZone>(true);
        }

        if (colliderSetup == null)
        {
            colliderSetup = GetComponent<CartColliderSetup>();
        }

        InitializeCells();
        RegisterExistingLoadedCargo();
    }

    public bool CanPlace(Vector3Int position, Vector3Int cargoSize)
    {
        return CanPlace(position, cargoSize, null);
    }

    public void Place(Vector3Int position, CargoInstance cargo)
    {
        if (cargo == null)
        {
            return;
        }

        Vector3Int cargoSize = ResolveCargoSize(cargo.GridSize);
        if (!CanPlace(position, cargoSize, cargo))
        {
            return;
        }

        ClearPlacement(cargo);
        SetPlacement(position, cargo, cargoSize);
        RegisterLoadedCargo(cargo);

        SnapCargoToPlacement(cargo, position, cargoSize);
        cargo.SetPresentationHidden(false);
        cargo.SetCurrentZone(cargoZone);

        NotifyInventoryChanged();
    }

    public CargoInstance Remove(Vector3Int position)
    {
        return Remove(position, false);
    }

    public CargoInstance Remove(Vector3Int position, bool keepLoaded)
    {
        if (!TryGetCargoAt(position, out CargoInstance cargo))
        {
            return null;
        }

        RemoveCargo(cargo, keepLoaded);
        return cargo;
    }

    public bool RemoveCargo(CargoInstance cargo, bool keepLoaded = false)
    {
        if (cargo == null)
        {
            return false;
        }

        ClearPlacement(cargo);

        if (!keepLoaded)
        {
            _loadedCargos.Remove(cargo);
            if (cargo.CurrentZone == cargoZone)
            {
                cargo.SetCurrentZone(null);
            }

            if (cargo.LoadedInventory == this)
            {
                cargo.SetLoadedInventory(null);
            }
        }

        NotifyInventoryChanged();
        return true;
    }

    public bool HandleCargoLost(CargoInstance cargo)
    {
        if (cargo == null)
        {
            return false;
        }

        bool isTrackedCargo = _loadedCargos.Contains(cargo) || cargo.LoadedInventory == this;
        if (!isTrackedCargo)
        {
            return false;
        }

        ClearPlacement(cargo);
        _loadedCargos.Remove(cargo);
        cargo.SetCurrentZone(null);
        cargo.SetLoadedInventory(null);
        cargo.MarkLostFromCart();
        NotifyInventoryChanged();
        OnCargoLost?.Invoke(cargo);
        return true;
    }

    public float GetTotalWeight()
    {
        SanitizeLoadedCargos();

        float totalWeight = 0f;
        foreach (CargoInstance cargo in _loadedCargos)
        {
            if (cargo != null)
            {
                totalWeight += cargo.PhysicalMass;
            }
        }

        return totalWeight;
    }

    public List<CargoInstance> GetLoadedCargos()
    {
        SanitizeLoadedCargos();

        List<CargoInstance> cargos = new List<CargoInstance>(_loadedCargos.Count);
        foreach (CargoInstance cargo in _loadedCargos)
        {
            if (cargo != null)
            {
                cargos.Add(cargo);
            }
        }

        return cargos;
    }

    public bool TryPlaceFromLocalPosition(CargoInstance cargo, Vector3 localPosition)
    {
        if (cargo == null)
        {
            return false;
        }

        SyncGridFromPhysics();

        Vector3Int cargoSize = ResolveCargoSize(cargo.GridSize);
        Vector3Int desiredPosition = ClampOriginToBounds(LocalCenterToGridPosition(localPosition, cargoSize), cargoSize);

        if (!CanPlace(desiredPosition, cargoSize, cargo)
            && !TryFindNearestFit(desiredPosition, cargoSize, cargo, out desiredPosition))
        {
            return false;
        }

        Place(desiredPosition, cargo);
        return true;
    }

    public void SyncGridFromPhysics()
    {
        ClearAllCells();
        _placements.Clear();
        SanitizeLoadedCargos();

        foreach (CargoInstance cargo in _loadedCargos)
        {
            if (cargo == null)
            {
                continue;
            }

            Vector3Int cargoSize = ResolveCargoSize(cargo.GridSize);
            if (!TryGetPlacementFromPhysics(cargo, cargoSize, out Vector3Int origin))
            {
                continue;
            }

            if (!CanPlace(origin, cargoSize, cargo)
                && !TryFindNearestFit(origin, cargoSize, cargo, out origin))
            {
                continue;
            }

            SetPlacement(origin, cargo, cargoSize);
            cargo.SetCurrentZone(cargoZone);
            cargo.SetLoadedInventory(this);
        }

        NotifyInventoryChanged();
    }

    public float GetCargoLossHeight()
    {
        float baseHeight = transform.position.y;

        if (colliderSetup != null)
        {
            baseHeight = colliderSetup.GetCargoBounds().min.y;
        }
        else if (cargoZone != null && cargoZone.TryGetComponent(out Collider zoneCollider))
        {
            baseHeight = zoneCollider.bounds.min.y;
        }

        return baseHeight - CargoPhysicsSettings.Load().FallDistanceThreshold;
    }

    public bool IsOccupied(Vector3Int position)
    {
        return TryGetCell(position, out GridCell cell) && cell.isOccupied;
    }

    public bool TryGetCargoAt(Vector3Int position, out CargoInstance cargo)
    {
        cargo = null;
        return TryGetCell(position, out GridCell cell) && cell.isOccupied && (cargo = cell.occupiedBy) != null;
    }

    public bool TryGetPlacement(Vector3Int position, out CargoInstance cargo, out Vector3Int origin, out Vector3Int size)
    {
        cargo = null;
        origin = default;
        size = Vector3Int.one;

        if (!TryGetCargoAt(position, out cargo) || cargo == null)
        {
            return false;
        }

        return TryGetPlacement(cargo, out origin, out size);
    }

    public bool TryGetPlacement(CargoInstance cargo, out Vector3Int origin, out Vector3Int size)
    {
        origin = default;
        size = Vector3Int.one;

        if (cargo == null || !_placements.TryGetValue(cargo, out CargoPlacement placement))
        {
            return false;
        }

        origin = placement.position;
        size = placement.size;
        return true;
    }

    public Vector3 GetLocalCenter(Vector3Int position, Vector3Int cargoSize)
    {
        Vector3Int resolvedSize = ResolveCargoSize(cargoSize);
        Vector3 origin = GetLocalMin(position);
        float size = CellWorldSize;

        return origin + new Vector3(
            resolvedSize.x * size * 0.5f,
            resolvedSize.y * size * 0.5f,
            resolvedSize.z * size * 0.5f);
    }

    public Vector3 GetGridCenterLocal()
    {
        Vector3Int dimensions = GridDimensions;
        return GetGridOriginLocal() + new Vector3(
            dimensions.x * CellWorldSize * 0.5f,
            dimensions.y * CellWorldSize * 0.5f,
            dimensions.z * CellWorldSize * 0.5f);
    }

    public bool IsInsideBounds(Vector3Int position)
    {
        Vector3Int dimensions = GridDimensions;
        return position.x >= 0 && position.y >= 0 && position.z >= 0
            && position.x < dimensions.x
            && position.y < dimensions.y
            && position.z < dimensions.z;
    }

    public Vector3 GetLocalMin(Vector3Int position)
    {
        return GetGridOriginLocal() + new Vector3(
            position.x * CellWorldSize,
            position.y * CellWorldSize,
            position.z * CellWorldSize);
    }

    public Vector3 GetWorldSize(Vector3Int cargoSize)
    {
        Vector3Int resolvedSize = ResolveCargoSize(cargoSize);
        return new Vector3(
            resolvedSize.x * CellWorldSize,
            resolvedSize.y * CellWorldSize,
            resolvedSize.z * CellWorldSize);
    }

    public Vector3 GetPlacementWorldSize(Vector3Int cargoSize)
    {
        Vector3 rawWorldSize = GetWorldSize(cargoSize);
        return new Vector3(
            Mathf.Max(0.05f, rawWorldSize.x - Mathf.Max(0f, cargoPlacementClearance.x)),
            Mathf.Max(0.05f, rawWorldSize.y - Mathf.Max(0f, cargoPlacementClearance.y)),
            Mathf.Max(0.05f, rawWorldSize.z - Mathf.Max(0f, cargoPlacementClearance.z)));
    }

    private void InitializeCells()
    {
        Vector3Int dimensions = GridDimensions;
        _cells = new GridCell[dimensions.x, dimensions.y, dimensions.z];

        for (int x = 0; x < dimensions.x; x++)
        {
            for (int y = 0; y < dimensions.y; y++)
            {
                for (int z = 0; z < dimensions.z; z++)
                {
                    _cells[x, y, z] = new GridCell(new Vector3Int(x, y, z));
                }
            }
        }
    }

    private bool CanPlace(Vector3Int position, Vector3Int cargoSize, CargoInstance cargoToIgnore)
    {
        Vector3Int dimensions = GridDimensions;
        Vector3Int resolvedSize = ResolveCargoSize(cargoSize);

        if (position.x < 0 || position.y < 0 || position.z < 0)
        {
            return false;
        }

        if (position.x + resolvedSize.x > dimensions.x
            || position.y + resolvedSize.y > dimensions.y
            || position.z + resolvedSize.z > dimensions.z)
        {
            return false;
        }

        for (int x = position.x; x < position.x + resolvedSize.x; x++)
        {
            for (int y = position.y; y < position.y + resolvedSize.y; y++)
            {
                for (int z = position.z; z < position.z + resolvedSize.z; z++)
                {
                    GridCell cell = _cells[x, y, z];
                    if (cell.isOccupied && cell.occupiedBy != cargoToIgnore)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private void RegisterExistingLoadedCargo()
    {
        CargoInstance[] cargos = FindObjectsByType<CargoInstance>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < cargos.Length; i++)
        {
            CargoInstance cargo = cargos[i];
            if (cargo == null || cargo.State != CargoState.Loaded)
            {
                continue;
            }

            if (cargo.LoadedInventory != null && cargo.LoadedInventory != this)
            {
                continue;
            }

            if (!IsInsideTrackedVolume(cargo) && cargo.CurrentZone != cargoZone)
            {
                continue;
            }

            RegisterLoadedCargo(cargo);
        }

        SyncGridFromPhysics();
    }

    private void RegisterLoadedCargo(CargoInstance cargo)
    {
        if (cargo == null)
        {
            return;
        }

        _loadedCargos.Add(cargo);
        cargo.SetLoadedInventory(this);
    }

    private void SanitizeLoadedCargos()
    {
        if (_loadedCargos.Count == 0)
        {
            return;
        }

        List<CargoInstance> stale = null;
        foreach (CargoInstance cargo in _loadedCargos)
        {
            if (cargo == null || cargo.State != CargoState.Loaded)
            {
                stale ??= new List<CargoInstance>();
                stale.Add(cargo);
            }
        }

        if (stale == null)
        {
            return;
        }

        for (int i = 0; i < stale.Count; i++)
        {
            _loadedCargos.Remove(stale[i]);
        }
    }

    public bool IsInsideTrackedVolume(CargoInstance cargo)
    {
        return IsInsideTrackedVolume(cargo, CellWorldSize * 1.5f);
    }

    public bool IsInsideTrackedVolume(CargoInstance cargo, float padding)
    {
        if (cargo == null)
        {
            return false;
        }

        Vector3 samplePoint = cargo.ItemRigidbody != null
            ? cargo.ItemRigidbody.worldCenterOfMass
            : cargo.transform.position;

        if (colliderSetup != null)
        {
            Bounds bounds = colliderSetup.GetCargoBounds();
            bounds.Expand(Mathf.Max(0f, padding));
            return bounds.Contains(samplePoint);
        }

        if (cargoZone == null || !cargoZone.TryGetComponent(out Collider zoneCollider))
        {
            return false;
        }

        Bounds zoneBounds = zoneCollider.bounds;
        zoneBounds.Expand(Mathf.Max(0f, padding));
        return zoneBounds.Contains(samplePoint);
    }

    private Vector3Int LocalCenterToGridPosition(Vector3 localPosition, Vector3Int cargoSize)
    {
        Vector3 normalized = (localPosition - GetGridOriginLocal()) / CellWorldSize;
        return new Vector3Int(
            Mathf.RoundToInt(normalized.x - (cargoSize.x * 0.5f)),
            Mathf.RoundToInt(normalized.y - (cargoSize.y * 0.5f)),
            Mathf.RoundToInt(normalized.z - (cargoSize.z * 0.5f)));
    }

    private void SetPlacement(Vector3Int position, CargoInstance cargo, Vector3Int cargoSize)
    {
        for (int x = position.x; x < position.x + cargoSize.x; x++)
        {
            for (int y = position.y; y < position.y + cargoSize.y; y++)
            {
                for (int z = position.z; z < position.z + cargoSize.z; z++)
                {
                    GridCell cell = _cells[x, y, z];
                    cell.isOccupied = true;
                    cell.occupiedBy = cargo;
                }
            }
        }

        _placements[cargo] = new CargoPlacement(position, cargoSize);
    }

    private void ClearPlacement(CargoInstance cargo)
    {
        if (cargo == null)
        {
            return;
        }

        _placements.Remove(cargo);

        if (_cells == null)
        {
            return;
        }

        for (int x = 0; x < _cells.GetLength(0); x++)
        {
            for (int y = 0; y < _cells.GetLength(1); y++)
            {
                for (int z = 0; z < _cells.GetLength(2); z++)
                {
                    GridCell cell = _cells[x, y, z];
                    if (cell.occupiedBy == cargo)
                    {
                        cell.isOccupied = false;
                        cell.occupiedBy = null;
                    }
                }
            }
        }
    }

    private void ClearAllCells()
    {
        if (_cells == null)
        {
            return;
        }

        for (int x = 0; x < _cells.GetLength(0); x++)
        {
            for (int y = 0; y < _cells.GetLength(1); y++)
            {
                for (int z = 0; z < _cells.GetLength(2); z++)
                {
                    GridCell cell = _cells[x, y, z];
                    cell.isOccupied = false;
                    cell.occupiedBy = null;
                }
            }
        }
    }

    private bool TryGetCell(Vector3Int position, out GridCell cell)
    {
        cell = null;
        if (_cells == null || !IsInsideBounds(position))
        {
            return false;
        }

        cell = _cells[position.x, position.y, position.z];
        return cell != null;
    }

    private Vector3 GetGridOriginLocal()
    {
        if (colliderSetup != null && colliderSetup.TryGetCargoBoundsLocal(CargoParent, out Bounds cargoBounds))
        {
            Vector3 origin = cargoBounds.min;
            Vector3 footprint = new Vector3(
                GridDimensions.x * CellWorldSize,
                0f,
                GridDimensions.z * CellWorldSize);
            Vector3 available = cargoBounds.size - footprint;

            origin.x += Mathf.Max(0f, available.x) * 0.5f;
            origin.z += Mathf.Max(0f, available.z) * 0.5f;

            return origin + gridOriginLocalOffset;
        }

        if (!deriveOriginFromCargoZone || cargoZone == null)
        {
            return gridOriginLocalOffset;
        }

        BoxCollider cargoZoneCollider = cargoZone.GetComponent<BoxCollider>();
        if (cargoZoneCollider == null)
        {
            return gridOriginLocalOffset;
        }

        Vector3 localMin = cargoZoneCollider.center - (cargoZoneCollider.size * 0.5f);
        Vector3 worldMin = cargoZoneCollider.transform.TransformPoint(localMin);
        return CargoParent.InverseTransformPoint(worldMin) + gridOriginLocalOffset;
    }

    private Vector3Int ResolveCargoSize(Vector3Int cargoSize)
    {
        return new Vector3Int(
            Mathf.Max(1, cargoSize.x),
            Mathf.Max(1, cargoSize.y),
            Mathf.Max(1, cargoSize.z));
    }

    private bool TryFindNearestFit(Vector3Int preferredPosition, Vector3Int cargoSize, CargoInstance cargoToIgnore, out Vector3Int position)
    {
        position = default;
        bool found = false;
        float bestDistance = float.MaxValue;

        Vector3Int dimensions = GridDimensions;
        Vector3Int resolvedSize = ResolveCargoSize(cargoSize);

        for (int x = 0; x <= dimensions.x - resolvedSize.x; x++)
        {
            for (int y = 0; y <= dimensions.y - resolvedSize.y; y++)
            {
                for (int z = 0; z <= dimensions.z - resolvedSize.z; z++)
                {
                    Vector3Int candidate = new Vector3Int(x, y, z);
                    if (!CanPlace(candidate, resolvedSize, cargoToIgnore))
                    {
                        continue;
                    }

                    float distance = (candidate - preferredPosition).sqrMagnitude;
                    if (distance >= bestDistance)
                    {
                        continue;
                    }

                    bestDistance = distance;
                    position = candidate;
                    found = true;
                }
            }
        }

        return found;
    }

    private bool TryGetPlacementFromPhysics(CargoInstance cargo, Vector3Int cargoSize, out Vector3Int origin)
    {
        origin = default;
        if (cargo == null)
        {
            return false;
        }

        if (cargo.TryGetBoundsInSpace(CargoParent, out Bounds localBounds))
        {
            origin = ClampOriginToBounds(LocalCenterToGridPosition(localBounds.center, cargoSize), cargoSize);
            return true;
        }

        Vector3 localPosition = CargoParent.InverseTransformPoint(cargo.transform.position);
        origin = ClampOriginToBounds(LocalCenterToGridPosition(localPosition, cargoSize), cargoSize);
        return true;
    }

    private Vector3Int ClampOriginToBounds(Vector3Int position, Vector3Int cargoSize)
    {
        Vector3Int dimensions = GridDimensions;
        Vector3Int resolvedSize = ResolveCargoSize(cargoSize);

        return new Vector3Int(
            Mathf.Clamp(position.x, 0, Mathf.Max(0, dimensions.x - resolvedSize.x)),
            Mathf.Clamp(position.y, 0, Mathf.Max(0, dimensions.y - resolvedSize.y)),
            Mathf.Clamp(position.z, 0, Mathf.Max(0, dimensions.z - resolvedSize.z)));
    }

    private void SnapCargoToPlacement(CargoInstance cargo, Vector3Int position, Vector3Int cargoSize)
    {
        if (cargo == null)
        {
            return;
        }

        Vector3 blockMinLocal = GetLocalMin(position);
        if (cargo.TryGetGridPlacement(CargoParent, blockMinLocal, out Vector3 localPosition, out Vector3 localScale))
        {
            cargo.LoadIntoCart(this, CargoParent, localPosition, localScale);
            return;
        }

        cargo.LoadIntoCart(this, CargoParent, GetLocalCenter(position, cargoSize));
    }

    private void NotifyInventoryChanged()
    {
        InventoryChanged?.Invoke();
    }
}
