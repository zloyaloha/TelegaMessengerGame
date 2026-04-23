using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Цель")]
    [SerializeField] private Transform target;

    [Header("Параметры")]
    [SerializeField] private float distance = 4f;
    [SerializeField] private float height = 2f;
    [SerializeField] private float sensitivity = 2f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 60f;

    [Header("Коллизии")]
    [SerializeField] private LayerMask collisionLayers = ~0;
    [SerializeField] private float collisionRadius = 0.25f;
    [SerializeField] private float collisionBuffer = 0.15f;
    [SerializeField] private float minCameraHeightOffset = 0.35f;
    [SerializeField] private float minNearClipPlane = 0.1f;

    [Header("Сглаживание")]
    [SerializeField] private float positionSmoothTime = 0.05f;
    [SerializeField] private float rotationSharpness = 18f;

    private float _yaw;
    private float _pitch;
    private Mouse _mouse;
    private Camera _controlledCamera;
    private Vector3 _positionVelocity;
    private readonly RaycastHit[] _cameraHits = new RaycastHit[8];

    private void Awake()
    {
        _controlledCamera = GetComponentInChildren<Camera>();
        _mouse = Mouse.current;

        Vector3 eulerAngles = transform.eulerAngles;
        _yaw = eulerAngles.y;
        _pitch = NormalizeAngle(eulerAngles.x);
    }

    private void Start()
    {
        if (target == null)
        {
            PlayerMovement playerMovement = FindFirstObjectByType<PlayerMovement>();
            if (playerMovement != null)
            {
                target = playerMovement.transform;
            }
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void LateUpdate()
    {
        if (target == null) return;
        if (_mouse == null)
        {
            _mouse = Mouse.current;
            if (_mouse == null) return;
        }

        Vector2 mouseDelta = _mouse.delta.ReadValue();

        _yaw += mouseDelta.x * sensitivity;
        _pitch -= mouseDelta.y * sensitivity;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        UpdateNearClip();

        Vector3 pivotPosition = target.position + Vector3.up * height;
        Quaternion orbitRotation = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 lookDirection = orbitRotation * Vector3.back;
        Vector3 desiredPosition = pivotPosition + (lookDirection * distance);
        Vector3 resolvedPosition = ResolveCollisions(pivotPosition, desiredPosition);
        Quaternion targetRotation = Quaternion.LookRotation(pivotPosition - resolvedPosition, Vector3.up);

        transform.position = Vector3.SmoothDamp(transform.position, resolvedPosition, ref _positionVelocity, positionSmoothTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime));
    }

    private Vector3 ResolveCollisions(Vector3 pivotPosition, Vector3 desiredPosition)
    {
        Vector3 direction = desiredPosition - pivotPosition;
        float desiredDistance = direction.magnitude;

        if (desiredDistance <= Mathf.Epsilon)
        {
            return desiredPosition;
        }

        direction /= desiredDistance;
        float resolvedDistance = desiredDistance;
        int hitCount = Physics.SphereCastNonAlloc(
            pivotPosition,
            collisionRadius,
            direction,
            _cameraHits,
            desiredDistance,
            collisionLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _cameraHits[i];
            if (hit.collider == null)
            {
                continue;
            }

            if (hit.collider.transform.IsChildOf(target))
            {
                continue;
            }

            resolvedDistance = Mathf.Min(resolvedDistance, Mathf.Max(0f, hit.distance - collisionBuffer));
        }

        Vector3 resolvedPosition = pivotPosition + (direction * resolvedDistance);
        float minHeight = target.position.y + minCameraHeightOffset;

        if (resolvedPosition.y < minHeight)
        {
            resolvedPosition.y = minHeight;
        }

        return resolvedPosition;
    }

    private void UpdateNearClip()
    {
        if (_controlledCamera != null && _controlledCamera.nearClipPlane > minNearClipPlane)
        {
            _controlledCamera.nearClipPlane = minNearClipPlane;
        }
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}
