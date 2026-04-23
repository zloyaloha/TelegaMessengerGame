using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerCarryController : MonoBehaviour
{
    [Header("Carry")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Vector3 carryLocalOffset = new Vector3(0f, -0.15f, 1.6f);
    [SerializeField] private float breakCarryDistance = 3.5f;
    [SerializeField] private float throwImpulse = 1.5f;

    private Keyboard _keyboard;
    private Transform _carryHoldPoint;
    private CharacterController _characterController;
    private CargoInstance _carriedItem;

    public bool IsCarrying => _carriedItem != null;
    public CargoInstance CarriedItem => _carriedItem;

    private void Awake()
    {
        _keyboard = Keyboard.current;
        _characterController = GetComponent<CharacterController>();

        EnsureHoldPoint();
    }

    private void Update()
    {
        if (_keyboard == null)
        {
            _keyboard = Keyboard.current;
            if (_keyboard == null)
            {
                return;
            }
        }

        EnsureHoldPoint();
        UpdateHoldPointPose();

        if (_keyboard.gKey.wasPressedThisFrame)
        {
            DropCarriedItem();
        }
    }

    private void LateUpdate()
    {
        if (_carriedItem == null || _carryHoldPoint == null)
        {
            return;
        }

        UpdateHoldPointPose();

        Rigidbody carriedBody = _carriedItem.ItemRigidbody;
        if (carriedBody == null)
        {
            NotifyCarriedItemUnavailable(_carriedItem);
            return;
        }

        Vector3 targetPosition = _carryHoldPoint.position;
        Vector3 toTarget = targetPosition - carriedBody.worldCenterOfMass;
        if (toTarget.magnitude > breakCarryDistance)
        {
            DropCarriedItem();
            return;
        }

        if (!carriedBody.isKinematic)
        {
            carriedBody.linearVelocity = Vector3.zero;
            carriedBody.angularVelocity = Vector3.zero;
        }

        carriedBody.position = targetPosition;
        carriedBody.rotation = _carryHoldPoint.rotation;
    }

    public bool TryPickUp(CargoInstance item)
    {
        if (item == null || _carriedItem != null)
        {
            return false;
        }

        EnsureHoldPoint();
        if (_carryHoldPoint == null)
        {
            return false;
        }

        if (!item.TryBeginCarry(this, _characterController))
        {
            return false;
        }

        _carriedItem = item;
        return true;
    }

    public void DropCarriedItem()
    {
        if (_carriedItem == null)
        {
            return;
        }

        Vector3 releasePosition = _carryHoldPoint != null
            ? _carryHoldPoint.position
            : transform.position + transform.forward;

        Quaternion releaseRotation = _carryHoldPoint != null
            ? _carryHoldPoint.rotation
            : transform.rotation;

        Vector3 releaseVelocity = (cameraTransform != null ? cameraTransform.forward : transform.forward) * throwImpulse;
        ReleaseCarriedItem(releasePosition, releaseRotation, releaseVelocity);
    }

    public CargoInstance ReleaseCarriedItem(Vector3 worldPosition, Quaternion worldRotation, Vector3 releaseVelocity)
    {
        if (_carriedItem == null)
        {
            return null;
        }

        CargoInstance releasedItem = _carriedItem;
        _carriedItem = null;
        releasedItem.Drop(worldPosition, worldRotation, releaseVelocity);
        return releasedItem;
    }

    public void NotifyCarriedItemUnavailable(CargoInstance item)
    {
        if (_carriedItem == item)
        {
            _carriedItem = null;
        }
    }

    private void EnsureHoldPoint()
    {
        if (_carryHoldPoint != null)
        {
            return;
        }

        GameObject holdPointObject = new GameObject("CarryHoldPoint");
        holdPointObject.hideFlags = HideFlags.HideInHierarchy;
        _carryHoldPoint = holdPointObject.transform;
        _carryHoldPoint.SetParent(null, false);
        UpdateHoldPointPose();
    }

    private void UpdateHoldPointPose()
    {
        if (_carryHoldPoint == null)
        {
            return;
        }

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        Vector3 holdForward = ResolveHoldForward();
        Vector3 holdRight = Vector3.Cross(Vector3.up, holdForward).normalized;
        float baseHoldHeight = ResolveBaseHoldHeight();

        Vector3 holdPosition = transform.position
            + (Vector3.up * (baseHoldHeight + carryLocalOffset.y))
            + (holdRight * carryLocalOffset.x)
            + (holdForward * carryLocalOffset.z);

        _carryHoldPoint.SetPositionAndRotation(holdPosition, Quaternion.LookRotation(holdForward, Vector3.up));
    }

    private Vector3 ResolveHoldForward()
    {
        Vector3 holdForward = cameraTransform != null ? cameraTransform.forward : transform.forward;
        holdForward = Vector3.ProjectOnPlane(holdForward, Vector3.up);

        if (holdForward.sqrMagnitude < 0.0001f)
        {
            holdForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        }

        return holdForward.sqrMagnitude > 0.0001f ? holdForward.normalized : Vector3.forward;
    }

    private float ResolveBaseHoldHeight()
    {
        if (_characterController == null)
        {
            return 1.75f;
        }

        return _characterController.center.y + (_characterController.height * 0.4f);
    }
}
