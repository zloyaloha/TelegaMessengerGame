using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CargoInstance))]
public class CargoFallDetector : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float checkInterval = 0.5f;

    private CargoInstance _cargo;
    private float _elapsed;

    private void Awake()
    {
        Initialize(GetComponent<CargoInstance>());
    }

    public void Initialize(CargoInstance cargo)
    {
        _cargo = cargo;
    }

    private void FixedUpdate()
    {
        if (_cargo == null || _cargo.State != CargoState.Loaded)
        {
            _elapsed = 0f;
            return;
        }

        _elapsed += Time.fixedDeltaTime;
        if (_elapsed < checkInterval)
        {
            return;
        }

        _elapsed = 0f;

        CartInventory inventory = _cargo.LoadedInventory;
        if (inventory == null)
        {
            return;
        }

        float cargoHeight = _cargo.ItemRigidbody != null
            ? _cargo.ItemRigidbody.worldCenterOfMass.y
            : transform.position.y;

        bool fellBelowCart = cargoHeight < inventory.GetCargoLossHeight();
        float volumePadding = Mathf.Max(0.05f, inventory.CellWorldSize * 0.35f);
        bool leftCargoVolume = !inventory.IsInsideTrackedVolume(_cargo, volumePadding);

        if (!fellBelowCart && !leftCargoVolume)
        {
            return;
        }

        inventory.HandleCargoLost(_cargo);
    }
}
