using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class CartPuller : MonoBehaviour
{
    [SerializeField] private Vector3 anchorLocalOffset = new Vector3(0f, 0f, 0f);
    [FormerlySerializedAs("anchorPositionSharpness")]
    [SerializeField, Min(0.1f)] private float detachedPositionSharpness = 24f;
    [SerializeField] private float anchorRotationSharpness = 12f;

    private CartController _attachedCart;
    private CharacterController _characterController;
    private Rigidbody _anchorBody;
    private Transform _anchorTransform;
    private Vector3 _activeAnchorLocalOffset;

    public bool HasAttachedCart => _attachedCart != null;
    public CartController AttachedCart => _attachedCart;
    public Vector3 AnchorPosition => _anchorBody != null
        ? _anchorBody.position
        : transform.TransformPoint(anchorLocalOffset);

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _activeAnchorLocalOffset = anchorLocalOffset;
        EnsureAnchorBody();
    }

    private void FixedUpdate()
    {
        if (_anchorBody == null)
        {
            EnsureAnchorBody();
            if (_anchorBody == null)
            {
                return;
            }
        }

        Vector3 targetPosition = transform.TransformPoint(GetCurrentAnchorLocalOffset());
        Quaternion targetRotation = Quaternion.LookRotation(transform.forward, Vector3.up);
        bool pullingCart = _attachedCart != null;

        Vector3 nextPosition;
        Quaternion nextRotation;

        if (pullingCart)
        {
            // Якорь теперь только ориентир для GetTowConnectionTargetPosition
            // (физический SpringJoint выключен). Lag по позиции при мгновенном
            // вращении приводил к тому, что towTarget смещался в сторону,
            // противоположную движению игрока, — телега пыталась рулить назад.
            // Позиция и вращение снэпятся к игроку без сглаживания.
            targetPosition.y = _attachedCart.HandlePosition.y;
            nextPosition = targetPosition;
            nextRotation = targetRotation;
        }
        else
        {
            float positionLerp = 1f - Mathf.Exp(-detachedPositionSharpness * Time.fixedDeltaTime);
            float rotationLerp = 1f - Mathf.Exp(-anchorRotationSharpness * Time.fixedDeltaTime);
            nextPosition = Vector3.Lerp(_anchorBody.position, targetPosition, positionLerp);
            nextRotation = Quaternion.Slerp(_anchorBody.rotation, targetRotation, rotationLerp);
        }

        _anchorBody.MovePosition(nextPosition);
        _anchorBody.MoveRotation(nextRotation);
    }

    private void OnDestroy()
    {
        if (_anchorTransform != null)
        {
            Destroy(_anchorTransform.gameObject);
        }
    }

    public bool AttachCart(CartController cart)
    {
        if (cart == null)
        {
            return false;
        }

        EnsureAnchorBody();
        if (_anchorBody == null)
        {
            return false;
        }

        if (_attachedCart != null && _attachedCart != cart)
        {
            DetachCurrentCart();
        }

        Vector3 handlePosition = cart.HandlePosition;
        Vector3 pullDirection = cart.HandlePullDirection;
        Quaternion anchorRotation = Quaternion.LookRotation(pullDirection.normalized, Vector3.up);
        Vector3 anchorPosition = handlePosition - (anchorRotation * cart.TowConnectedAnchorOffset);
        SetPullPose(anchorPosition, anchorRotation);
        _activeAnchorLocalOffset = anchorLocalOffset;
        _anchorTransform.position = anchorPosition;
        _anchorBody.position = anchorPosition;
        _anchorBody.rotation = anchorRotation;

        if (!cart.AttachToPuller(this, _anchorBody, _characterController))
        {
            ResetActiveAnchorOffset();
            return false;
        }

        _attachedCart = cart;
        return true;
    }

    public void DetachCurrentCart()
    {
        if (_attachedCart == null)
        {
            return;
        }

        CartController cart = _attachedCart;
        _attachedCart = null;
        ResetActiveAnchorOffset();
        cart.DetachFromPuller(this);
    }

    public void NotifyCartUnavailable(CartController cart)
    {
        if (_attachedCart == cart)
        {
            _attachedCart = null;
            ResetActiveAnchorOffset();
        }
    }

    public float GetMovementSpeedMultiplier(Vector3 moveDirection, Vector3 groundNormal, bool sprinting)
    {
        return _attachedCart != null
            ? _attachedCart.EvaluateSpeedMultiplier(moveDirection, groundNormal, sprinting)
            : 1f;
    }

    public Vector3 ConstrainMotionToAttachedCart(Vector3 requestedMotion)
    {
        if (_attachedCart == null || requestedMotion.sqrMagnitude <= 0.000001f)
        {
            return requestedMotion;
        }

        Vector3 planarMotion = Vector3.ProjectOnPlane(requestedMotion, Vector3.up);
        if (planarMotion.sqrMagnitude <= 0.000001f)
        {
            return requestedMotion;
        }

        Vector3 verticalMotion = requestedMotion - planarMotion;
        Vector3 handlePosition = _attachedCart.HandlePosition;
        Vector3 currentConstraintPoint = GetClosestConstraintPoint(handlePosition);
        Vector3 currentOffset = Vector3.ProjectOnPlane(currentConstraintPoint - handlePosition, Vector3.up);
        Vector3 requestedOffset = currentOffset + planarMotion;
        float slack = Mathf.Max(0.01f, _attachedCart.MovementTowSlack);
        Vector3 clampedOffset = Vector3.ClampMagnitude(requestedOffset, slack);
        Vector3 allowedPlanarMotion = clampedOffset - currentOffset;
        return verticalMotion + allowedPlanarMotion;
    }

    private Vector3 GetClosestConstraintPoint(Vector3 worldPoint)
    {
        if (_characterController != null)
        {
            Bounds bounds = _characterController.bounds;
            if (bounds.extents.sqrMagnitude > 0.0001f)
            {
                return bounds.ClosestPoint(worldPoint);
            }
        }

        return transform.position;
    }

    private void EnsureAnchorBody()
    {
        if (_anchorTransform == null)
        {
            GameObject anchorObject = new GameObject($"{name}_CartPullAnchor");
            anchorObject.hideFlags = HideFlags.HideInHierarchy;
            _anchorTransform = anchorObject.transform;
        }

        if (_anchorBody == null)
        {
            _anchorBody = _anchorTransform.GetComponent<Rigidbody>();
            if (_anchorBody == null)
            {
                _anchorBody = _anchorTransform.gameObject.AddComponent<Rigidbody>();
            }

            _anchorBody.isKinematic = true;
            _anchorBody.useGravity = false;
            _anchorBody.detectCollisions = false;
            _anchorBody.interpolation = RigidbodyInterpolation.Interpolate;
        }

        _anchorTransform.position = transform.TransformPoint(GetCurrentAnchorLocalOffset());
        _anchorTransform.rotation = Quaternion.LookRotation(transform.forward, Vector3.up);
    }

    private Vector3 GetCurrentAnchorLocalOffset()
    {
        return _attachedCart != null ? _activeAnchorLocalOffset : anchorLocalOffset;
    }

    private void ResetActiveAnchorOffset()
    {
        _activeAnchorLocalOffset = anchorLocalOffset;
    }

    private void SetPullPose(Vector3 anchorPosition, Quaternion anchorRotation)
    {
        Vector3 playerPosition = anchorPosition - (anchorRotation * anchorLocalOffset);
        // Сохраняем вертикальное положение игрока: якорь располагается на высоте ручки,
        // и без этого SetPullPose подбрасывал бы игрока к ручке, а затем он падал бы,
        // создавая рывок по всей связке.
        playerPosition.y = transform.position.y;

        if (_characterController == null)
        {
            transform.SetPositionAndRotation(playerPosition, anchorRotation);
            return;
        }

        bool controllerWasEnabled = _characterController.enabled;
        if (controllerWasEnabled)
        {
            _characterController.enabled = false;
        }

        transform.SetPositionAndRotation(playerPosition, anchorRotation);

        if (controllerWasEnabled)
        {
            _characterController.enabled = true;
        }
    }

}
