using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerInteractor : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float interactionDistance = 2.75f;
    [SerializeField] private float interactionRadius = 0.65f;
    [SerializeField] private float interactionHeight = 1.1f;
    [SerializeField, Range(20f, 180f)] private float maxViewAngle = 95f;
    [SerializeField] private LayerMask interactionMask = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    private readonly RaycastHit[] _hits = new RaycastHit[16];
    private readonly Collider[] _overlapHits = new Collider[24];

    private Keyboard _keyboard;
    private PlayerCarryController _carryController;

    public PlayerCarryController CarryController => _carryController != null
        ? _carryController
        : (_carryController = GetComponent<PlayerCarryController>());

    public Transform CameraTransform => cameraTransform;

    private void Awake()
    {
        _keyboard = Keyboard.current;
        _carryController = GetComponent<PlayerCarryController>();
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

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (cameraTransform == null)
        {
            return;
        }

        bool interactPressed = _keyboard.eKey.wasPressedThisFrame
            || (_keyboard.gKey.wasPressedThisFrame && (CarryController == null || !CarryController.IsCarrying));

        if (interactPressed)
        {
            bool interacted = TryInteract();
            if (!interacted && _keyboard.eKey.wasPressedThisFrame && CarryController != null && CarryController.IsCarrying)
            {
                CarryController.DropCarriedItem();
            }
        }
    }

    private bool TryInteract()
    {
        IInteractable bestInteractable = null;
        float bestScore = float.MaxValue;

        Vector3 interactionOrigin = transform.position + (Vector3.up * interactionHeight);
        Vector3 aimOrigin = cameraTransform != null ? cameraTransform.position : interactionOrigin;
        Vector3 aimDirection = cameraTransform != null ? cameraTransform.forward : transform.forward;

        Ray ray = new Ray(aimOrigin, aimDirection);
        int rayHitCount = Physics.SphereCastNonAlloc(
            ray,
            interactionRadius,
            _hits,
            interactionDistance + interactionRadius,
            interactionMask,
            triggerInteraction);

        for (int i = 0; i < rayHitCount; i++)
        {
            RaycastHit hit = _hits[i];
            if (hit.collider == null)
            {
                continue;
            }

            if (!TryScoreInteractable(
                    hit.collider,
                    interactionOrigin,
                    aimOrigin,
                    aimDirection,
                    out IInteractable interactable,
                    out float score))
            {
                continue;
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestInteractable = interactable;
            }
        }

        int nearbyCount = Physics.OverlapSphereNonAlloc(
            interactionOrigin,
            interactionDistance,
            _overlapHits,
            interactionMask,
            triggerInteraction);

        for (int i = 0; i < nearbyCount; i++)
        {
            Collider nearbyCollider = _overlapHits[i];
            if (nearbyCollider == null)
            {
                continue;
            }

            if (!TryScoreInteractable(
                    nearbyCollider,
                    interactionOrigin,
                    aimOrigin,
                    aimDirection,
                    out IInteractable interactable,
                    out float score))
            {
                continue;
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestInteractable = interactable;
            }
        }

        if (bestInteractable == null)
        {
            return false;
        }

        bestInteractable.Interact(this);
        return true;
    }

    private bool TryScoreInteractable(
        Collider collider,
        Vector3 interactionOrigin,
        Vector3 aimOrigin,
        Vector3 aimDirection,
        out IInteractable interactable,
        out float score)
    {
        interactable = null;
        score = float.MaxValue;

        if (collider == null || collider.transform.IsChildOf(transform))
        {
            return false;
        }

        interactable = ResolveInteractable(collider);
        if (interactable == null || !interactable.CanInteract(this))
        {
            return false;
        }

        Vector3 closestPoint = collider.ClosestPoint(interactionOrigin);
        Vector3 toPointFromPlayer = closestPoint - interactionOrigin;
        float playerDistance = toPointFromPlayer.magnitude;
        if (playerDistance > interactionDistance + 0.05f)
        {
            return false;
        }

        Vector3 playerForward = transform.forward;
        float playerAngle = Vector3.Angle(playerForward, playerDistance > 0.001f ? toPointFromPlayer : playerForward);

        Vector3 toPointFromAim = closestPoint - aimOrigin;
        float cameraAngle = Vector3.Angle(aimDirection, toPointFromAim.sqrMagnitude > 0.001f ? toPointFromAim : aimDirection);

        if (playerAngle > maxViewAngle && cameraAngle > maxViewAngle)
        {
            return false;
        }

        float anglePenalty = Mathf.Min(playerAngle, cameraAngle) * 0.025f;
        score = playerDistance + anglePenalty;
        return true;
    }

    private static IInteractable ResolveInteractable(Collider collider)
    {
        MonoBehaviour[] behaviours = collider.GetComponentsInParent<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IInteractable interactable)
            {
                return interactable;
            }
        }

        return null;
    }
}
