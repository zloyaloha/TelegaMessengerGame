using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CartCargoZone : MonoBehaviour
{
    [SerializeField] private CartController ownerCart;
    [SerializeField] private Transform cargoParent;
    [SerializeField] private Vector3 defaultLocalLoadPosition;

    private readonly HashSet<CargoInstance> _items = new HashSet<CargoInstance>();
    private CartInventory _inventory;

    public float TotalMass { get; private set; }
    public int ItemCount => _items.Count;

    private void Awake()
    {
        if (ownerCart == null)
        {
            ownerCart = GetComponentInParent<CartController>();
        }

        if (_inventory == null && ownerCart != null)
        {
            _inventory = ownerCart.GetComponent<CartInventory>();
        }
    }

    private void OnEnable()
    {
        RegisterLoadedChildren();
    }

    private void OnTriggerEnter(Collider other)
    {
        CargoInstance item = other.GetComponentInParent<CargoInstance>();
        TryRegisterLoadedChild(item);
    }

    private void OnTriggerExit(Collider other)
    {
        CargoInstance item = other.GetComponentInParent<CargoInstance>();
        if (item != null && item.CurrentZone == this && item.State != CargoState.Loaded)
        {
            ForceRemove(item);
        }
    }

    public Vector3 GetLocalLoadPosition(Vector3 worldPosition)
    {
        Transform targetParent = ResolveCargoParent();
        return targetParent != null ? targetParent.InverseTransformPoint(worldPosition) : defaultLocalLoadPosition;
    }

    public bool TryRegister(CargoInstance item)
    {
        return TryRegister(item, defaultLocalLoadPosition);
    }

    public bool TryRegister(CargoInstance item, Vector3 localPosition)
    {
        if (item == null || item.IsBroken)
        {
            return false;
        }

        if (item.CurrentZone != null && item.CurrentZone != this)
        {
            item.CurrentZone.ForceRemove(item);
        }

        if (_inventory != null)
        {
            return _inventory.TryPlaceFromLocalPosition(item, localPosition);
        }

        Transform targetParent = ResolveCargoParent();
        if (targetParent == null)
        {
            return false;
        }

        item.LoadIntoCart(null, targetParent, localPosition);
        _items.Add(item);
        if (item.CurrentZone != this)
        {
            item.SetCurrentZone(this);
        }

        RecalculateMass();
        return true;
    }

    public void ForceRemove(CargoInstance item)
    {
        if (item == null)
        {
            return;
        }

        if (_items.Remove(item))
        {
            if (item.CurrentZone == this)
            {
                item.SetCurrentZone(null);
            }

            RecalculateMass();
        }
    }

    private void RecalculateMass()
    {
        float totalMass = 0f;

        foreach (CargoInstance item in _items)
        {
            if (item == null)
            {
                continue;
            }

            totalMass += item.PhysicalMass;
        }

        TotalMass = totalMass;
    }

    private Transform ResolveCargoParent()
    {
        if (cargoParent != null)
        {
            return cargoParent;
        }

        return ownerCart != null ? ownerCart.transform : transform;
    }

    private void RegisterLoadedChildren()
    {
        _items.Clear();

        if (_inventory != null)
        {
            List<CargoInstance> cargos = _inventory.GetLoadedCargos();
            for (int i = 0; i < cargos.Count; i++)
            {
                TryRegisterLoadedChild(cargos[i]);
            }

            RecalculateMass();
            return;
        }

        CargoInstance[] loadedItems = FindObjectsByType<CargoInstance>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < loadedItems.Length; i++)
        {
            TryRegisterLoadedChild(loadedItems[i]);
        }

        RecalculateMass();
    }

    private void TryRegisterLoadedChild(CargoInstance item)
    {
        if (item == null || item.State != CargoState.Loaded)
        {
            return;
        }

        if (!IsInsideZone(item))
        {
            return;
        }

        _items.Add(item);
        item.SetCurrentZone(this);
    }

    private bool IsInsideZone(CargoInstance item)
    {
        Collider zoneCollider = GetComponent<Collider>();
        if (zoneCollider == null || item == null)
        {
            return false;
        }

        Vector3 samplePoint = item.ItemRigidbody != null
            ? item.ItemRigidbody.worldCenterOfMass
            : item.transform.position;

        Bounds bounds = zoneCollider.bounds;
        bounds.Expand(0.25f);
        return bounds.Contains(samplePoint);
    }
}
