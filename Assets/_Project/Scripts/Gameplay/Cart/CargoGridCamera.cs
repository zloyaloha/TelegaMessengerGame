using System;
using UnityEngine;

[DisallowMultipleComponent]
public class CargoGridCamera : MonoBehaviour
{
    private enum TransitionState
    {
        Closed,
        Opening,
        Open,
        Closing
    }

    [Header("References")]
    [SerializeField] private CartInventory cartInventory;
    [SerializeField] private Camera gridCamera;

    [Header("View")]
    [SerializeField] private Vector3 offset = new Vector3(1.8f, 2.2f, -2.4f);
    [SerializeField] private Vector3 rotation = new Vector3(45f, 45f, 0f);
    [SerializeField] private bool alignToCartYaw = true;
    [SerializeField] private bool ignoreCartTilt = true;
    [SerializeField] private bool lookAtGridCenter = true;
    [SerializeField, Min(0.01f)] private float transitionDuration = 0.25f;

    private Camera _sourceCamera;
    private Behaviour _sourceCameraController;
    private Vector3 _openStartPosition;
    private Quaternion _openStartRotation;
    private float _openStartFieldOfView;
    private Vector3 _closeStartPosition;
    private Quaternion _closeStartRotation;
    private float _closeStartFieldOfView;
    private Vector3 _returnPosition;
    private Quaternion _returnRotation;
    private float _returnFieldOfView;
    private float _transitionProgress;
    private TransitionState _transitionState;

    public event Action Closed;

    public Camera ActiveCamera => gridCamera;
    public bool IsOpen => _transitionState != TransitionState.Closed;

    private void Awake()
    {
        if (cartInventory == null)
        {
            cartInventory = GetComponentInParent<CartInventory>();
        }

        EnsureGridCamera();
    }

    private void LateUpdate()
    {
        if (gridCamera == null)
        {
            return;
        }

        switch (_transitionState)
        {
            case TransitionState.Opening:
                UpdateOpeningTransition();
                break;

            case TransitionState.Open:
                ApplyPose(GetTargetPosition(), GetTargetRotation(), gridCamera.fieldOfView);
                break;

            case TransitionState.Closing:
                UpdateClosingTransition();
                break;
        }
    }

    public void Open(Camera sourceCamera, Behaviour sourceCameraController)
    {
        EnsureGridCamera();

        _sourceCamera = sourceCamera;
        _sourceCameraController = sourceCameraController;

        if (_sourceCamera != null)
        {
            _openStartPosition = _sourceCamera.transform.position;
            _openStartRotation = _sourceCamera.transform.rotation;
            _openStartFieldOfView = _sourceCamera.fieldOfView;
            _returnPosition = _sourceCamera.transform.position;
            _returnRotation = _sourceCamera.transform.rotation;
            _returnFieldOfView = _sourceCamera.fieldOfView;
            CopyLensSettings(_sourceCamera, gridCamera);
            _sourceCamera.enabled = false;
        }
        else
        {
            _openStartPosition = GetTargetPosition();
            _openStartRotation = GetTargetRotation();
            _openStartFieldOfView = gridCamera.fieldOfView;
            _returnPosition = _openStartPosition;
            _returnRotation = _openStartRotation;
            _returnFieldOfView = _openStartFieldOfView;
        }

        if (_sourceCameraController != null)
        {
            _sourceCameraController.enabled = false;
        }

        gridCamera.gameObject.SetActive(true);
        gridCamera.enabled = true;
        ApplyPose(_openStartPosition, _openStartRotation, _openStartFieldOfView);

        _transitionProgress = 0f;
        _transitionState = TransitionState.Opening;
    }

    public void Close()
    {
        if (_transitionState == TransitionState.Closed || gridCamera == null)
        {
            RestoreSourceCamera();
            Closed?.Invoke();
            return;
        }

        _closeStartPosition = gridCamera.transform.position;
        _closeStartRotation = gridCamera.transform.rotation;
        _closeStartFieldOfView = gridCamera.fieldOfView;
        _transitionProgress = 0f;
        _transitionState = TransitionState.Closing;
    }

    public void ForceRestore()
    {
        _transitionState = TransitionState.Closed;

        if (gridCamera != null)
        {
            gridCamera.enabled = false;
        }

        RestoreSourceCamera();
        Closed?.Invoke();
    }

    private void EnsureGridCamera()
    {
        if (gridCamera != null)
        {
            return;
        }

        Transform existingChild = transform.Find("CargoGridCamera");
        GameObject cameraObject;
        if (existingChild != null)
        {
            cameraObject = existingChild.gameObject;
        }
        else
        {
            cameraObject = new GameObject("CargoGridCamera");
            cameraObject.transform.SetParent(transform, false);
        }

        gridCamera = cameraObject.GetComponent<Camera>();
        if (gridCamera == null)
        {
            gridCamera = cameraObject.AddComponent<Camera>();
        }

        gridCamera.enabled = false;
        cameraObject.tag = "Untagged";
    }

    private void UpdateOpeningTransition()
    {
        _transitionProgress = Mathf.Clamp01(_transitionProgress + (Time.unscaledDeltaTime / transitionDuration));
        float t = SmoothStep(_transitionProgress);

        Vector3 targetPosition = Vector3.Lerp(_openStartPosition, GetTargetPosition(), t);
        Quaternion targetRotation = Quaternion.Slerp(_openStartRotation, GetTargetRotation(), t);
        float targetFov = Mathf.Lerp(_openStartFieldOfView, _openStartFieldOfView, t);
        ApplyPose(targetPosition, targetRotation, targetFov);

        if (_transitionProgress >= 1f)
        {
            _transitionState = TransitionState.Open;
        }
    }

    private void UpdateClosingTransition()
    {
        _transitionProgress = Mathf.Clamp01(_transitionProgress + (Time.unscaledDeltaTime / transitionDuration));
        float t = SmoothStep(_transitionProgress);

        Vector3 targetPosition = Vector3.Lerp(_closeStartPosition, _returnPosition, t);
        Quaternion targetRotation = Quaternion.Slerp(_closeStartRotation, _returnRotation, t);
        float targetFov = Mathf.Lerp(_closeStartFieldOfView, _returnFieldOfView, t);
        ApplyPose(targetPosition, targetRotation, targetFov);

        if (_transitionProgress >= 1f)
        {
            _transitionState = TransitionState.Closed;
            gridCamera.enabled = false;
            RestoreSourceCamera();
            Closed?.Invoke();
        }
    }

    private void ApplyPose(Vector3 position, Quaternion rotationQuaternion, float fieldOfView)
    {
        if (gridCamera == null)
        {
            return;
        }

        gridCamera.transform.SetPositionAndRotation(position, rotationQuaternion);
        gridCamera.fieldOfView = fieldOfView;
    }

    private void RestoreSourceCamera()
    {
        if (_sourceCamera != null)
        {
            _sourceCamera.enabled = true;
        }

        if (_sourceCameraController != null)
        {
            _sourceCameraController.enabled = true;
        }
    }

    private Vector3 GetTargetPosition()
    {
        return GetWorldCenter() + (GetReferenceRotation() * offset);
    }

    private Quaternion GetTargetRotation()
    {
        if (lookAtGridCenter)
        {
            Vector3 forward = GetWorldCenter() - GetTargetPosition();
            if (forward.sqrMagnitude > 0.0001f)
            {
                Quaternion lookRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                if (Mathf.Abs(rotation.z) > 0.001f)
                {
                    lookRotation *= Quaternion.Euler(0f, 0f, rotation.z);
                }

                return lookRotation;
            }
        }

        return GetReferenceRotation() * Quaternion.Euler(rotation);
    }

    private Vector3 GetWorldCenter()
    {
        Vector3 localCenter = cartInventory != null ? cartInventory.GetGridCenterLocal() : Vector3.zero;
        return transform.TransformPoint(localCenter);
    }

    private Quaternion GetReferenceRotation()
    {
        if (!alignToCartYaw)
        {
            return Quaternion.identity;
        }

        Vector3 forward = transform.forward;
        if (ignoreCartTilt)
        {
            forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        }

        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    private static void CopyLensSettings(Camera source, Camera target)
    {
        if (source == null || target == null)
        {
            return;
        }

        target.nearClipPlane = source.nearClipPlane;
        target.farClipPlane = source.farClipPlane;
        target.fieldOfView = source.fieldOfView;
        target.clearFlags = source.clearFlags;
        target.backgroundColor = source.backgroundColor;
        target.cullingMask = source.cullingMask;
    }

    private static float SmoothStep(float value)
    {
        return value * value * (3f - (2f * value));
    }
}
