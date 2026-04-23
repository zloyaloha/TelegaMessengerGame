using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class CartController : MonoBehaviour, IInteractable
{
    [Header("References")]
    [SerializeField] private Rigidbody cartRigidbody;
    [SerializeField] private SpringJoint pullJoint;
    [SerializeField] private Transform handleTransform;
    [SerializeField] private CartCargoZone cargoZone;
    [SerializeField] private CartInventory cartInventory;
    [SerializeField] private CargoGridInput cargoGridInput;
    [SerializeField] private Transform cargoDropPoint;
    [SerializeField] private Durability durability;
    [SerializeField] private WorldDurabilityLabel durabilityLabel;
    [SerializeField] private Collider[] cartColliders;
    [SerializeField] private Renderer[] cartRenderers;

    [Header("Pulling")]
    [SerializeField, Min(1f)] private float baseCartMass = 18f;
    [SerializeField, Min(5f)] private float loadForMinimumSpeed = 75f;
    [SerializeField] private float emptyCartSpeedMultiplier = 0.92f;
    [SerializeField] private float minimumSpeedMultiplier = 0.35f;
    [SerializeField] private float slopePenalty = 0.22f;
    [SerializeField] private float turnPenalty = 0.16f;
    [SerializeField] private float sprintPenalty = 0.08f;
    [SerializeField] private float damagePenalty = 0.18f;
    [SerializeField] private float maxAttachmentStretch = 2.1f;
    [SerializeField, Range(0f, 0.5f)] private float towStrainPenalty = 0.22f;
    [SerializeField, Range(0f, 0.5f)] private float cargoStabilitySpeedPenalty = 0.14f;
    [SerializeField, Range(0f, 0.6f)] private float sidePullSpeedPenalty = 0.18f;

    [Header("Handle Access")]
    [SerializeField, Min(0.1f)] private float handleAttachRadius = 0.75f;
    [SerializeField, Min(0.1f)] private float handleInteractRadius = 1.25f;
    [SerializeField, Min(0.25f)] private float handleDetachDistance = 2.05f;
    [SerializeField, Min(0f)] private float handleVerticalTolerance = 1.35f;
    [SerializeField] private Vector3 handleGripLocalOffset = new Vector3(0f, 0f, -1.1f);
    [SerializeField, Min(0f)] private float handleGripSurfaceOffset = 0.08f;

    [Header("Rigid Tow Joint")]
    [SerializeField, Min(0f)] private float rigidTowBehindOffset = 0.35f;
    [SerializeField, Min(0.001f)] private float rigidTowAllowedError = 0.08f;
    [SerializeField, Min(0.02f)] private float visibleTowSlack = 0.55f;
    [SerializeField, Min(0.02f)] private float movementTowSlack = 1.05f;
    [SerializeField, Min(0f)] private float towJointSpring = 50000f;
    [SerializeField, Min(0f)] private float towJointDamper = 12000f;
    [SerializeField, Min(0f)] private float towJointMinDistance = 0f;
    [SerializeField, Min(0f)] private float towJointMaxDistance = 0f;
    [SerializeField, Min(0f)] private float towJointTolerance = 0f;
    [SerializeField, Min(0.01f)] private float towJointMassScale = 4f;
    [SerializeField, Min(0.01f)] private float towJointConnectedMassScale = 1f;
    [SerializeField, Min(1)] private int towSolverIterations = 12;
    [SerializeField, Min(1)] private int towSolverVelocityIterations = 4;

    [Header("Planar Tow Motor")]
    [SerializeField, Min(0f)] private float towMotorSpring = 48f;
    [SerializeField, Min(0f)] private float towMotorDamper = 11f;
    [SerializeField, Min(0f)] private float towMotorMaxAcceleration = 70f;
    [SerializeField, Min(0f)] private float towMotorDeadZone = 0.025f;
    [SerializeField, Range(0f, 1f)] private float handleSteeringForceFactor = 1f;

    [Header("Transport Physics")]
    [SerializeField, Min(0f)] private float rollingResistance = 0.45f;
    [SerializeField, Min(0f)] private float lateralGrip = 9f;
    [SerializeField, Min(0f)] private float lateralVelocityCorrection = 6f;
    [SerializeField, Min(0f)] private float brakeDamping = 1.35f;
    [SerializeField, Min(0f)] private float yawAlignmentTorque = 14f;
    [SerializeField, Range(0f, 3f)] private float attachedYawAlignmentAssist = 2f;
    [SerializeField, Min(0f)] private float yawAlignmentDamping = 3f;
    [SerializeField, Min(0f)] private float stoppedAnchorBrakeSpeed = 0.25f;
    [SerializeField, Min(0f)] private float stoppedVelocitySnap = 0.04f;

    [Header("Stability")]
    [SerializeField] private Vector3 centerOfMassOffset = new Vector3(0f, -0.35f, 0f);
    [SerializeField] private float uprightTorque = 24f;
    [SerializeField] private float stableAngularDamping = 4f;
    [SerializeField] private float criticalStabilityMultiplier = 0.45f;
    [SerializeField] private float destroyedStabilityMultiplier = 0.15f;

    [Header("Damage Visuals")]
    [SerializeField] private Color healthyColor = new Color(0.5f, 0.35f, 0.2f, 1f);
    [SerializeField] private Color damagedColor = new Color(0.75f, 0.4f, 0.25f, 1f);
    [SerializeField] private Color destroyedColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    private CartPuller _attachedPuller;
    private Rigidbody _connectedAnchorBody;
    private Vector3 _defaultJointConnectedAnchor;
    private float _defaultJointMassScale;
    private float _defaultJointConnectedMassScale;
    private float _currentHandleStretch;
    private float _anchorPlanarSpeed;
    private float _towStrain01;
    private float _currentLoadRatio;
    private float _cargoStabilityRisk;
    private Vector3 _anchorPlanarVelocity;
    private bool _hasLastAnchorPosition;
    private Vector3 _lastAnchorPosition;
    private BoxCollider _handleBoxCollider;

    public float CurrentCargoMass => cartInventory != null
        ? cartInventory.GetTotalWeight()
        : (cargoZone != null ? cargoZone.TotalMass : 0f);
    public bool IsBroken => durability != null && durability.IsDestroyed;
    public bool IsAttached => _attachedPuller != null;
    public float BaseCartMass => baseCartMass;
    public Durability Durability => durability != null ? durability : (durability = GetComponent<Durability>());
    public float HpPercent => Durability != null ? Durability.NormalizedDurability : 1f;
    public CartInventory Inventory => cartInventory != null ? cartInventory : (cartInventory = GetComponent<CartInventory>());
    public CargoGridInput CargoGridInput => cargoGridInput != null ? cargoGridInput : (cargoGridInput = GetComponent<CargoGridInput>());
    public Rigidbody CartRigidbody => cartRigidbody != null ? cartRigidbody : (cartRigidbody = GetComponent<Rigidbody>());
    public Vector3 HandlePosition => GetHandleGripPosition();
    public Vector3 HandlePullDirection => GetHandlePullDirection();
    public float TowStrain01 => _towStrain01;
    public float CurrentLoadRatio => _currentLoadRatio;
    public float CargoStabilityRisk => _cargoStabilityRisk;
    public float VisibleTowSlack => Mathf.Max(rigidTowAllowedError, visibleTowSlack);
    public float MovementTowSlack => Mathf.Max(VisibleTowSlack, movementTowSlack);
    public Vector3 TowConnectedAnchorOffset => GetConnectedAnchorOffset();

    private void Awake()
    {
        if (cartRigidbody == null)
        {
            cartRigidbody = GetComponent<Rigidbody>();
        }

        DestroyLegacyConfigurableJoint();

        ResolvePullJoint(true);

        if (cargoZone == null)
        {
            cargoZone = GetComponentInChildren<CartCargoZone>(true);
        }

        if (cartInventory == null)
        {
            cartInventory = GetComponent<CartInventory>();
        }

        if (cargoGridInput == null)
        {
            cargoGridInput = GetComponent<CargoGridInput>();
        }

        if (durability == null)
        {
            durability = GetComponent<Durability>();
        }

        if (cartColliders == null || cartColliders.Length == 0)
        {
            cartColliders = GetComponentsInChildren<Collider>(true);
        }

        if (cartRenderers == null || cartRenderers.Length == 0)
        {
            cartRenderers = GetComponentsInChildren<Renderer>(true);
        }

        if (durabilityLabel == null)
        {
            durabilityLabel = GetComponent<WorldDurabilityLabel>();
        }

        if (durabilityLabel == null)
        {
            durabilityLabel = gameObject.AddComponent<WorldDurabilityLabel>();
        }

        if (handleTransform == null)
        {
            Transform locatedHandle = transform.Find("Handle");
            handleTransform = locatedHandle != null ? locatedHandle : transform;
        }

        if (handleTransform != null)
        {
            _handleBoxCollider = handleTransform.GetComponent<BoxCollider>();
        }

        if (cargoDropPoint == null)
        {
            Transform locatedDropPoint = transform.Find("CargoDropPoint");
            cargoDropPoint = locatedDropPoint != null ? locatedDropPoint : handleTransform;
        }

        cartRigidbody.mass = baseCartMass;
        cartRigidbody.centerOfMass = centerOfMassOffset;
        cartRigidbody.maxAngularVelocity = 18f;
        cartRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        cartRigidbody.solverIterations = Mathf.Max(cartRigidbody.solverIterations, towSolverIterations);
        cartRigidbody.solverVelocityIterations = Mathf.Max(cartRigidbody.solverVelocityIterations, towSolverVelocityIterations);
        UpdateCargoMassContribution();

        durabilityLabel.Initialize(durability, cartRenderers);
        ConfigureJointDefaults();
        UpdateDamageTint();
    }

    private void OnEnable()
    {
        if (durability != null)
        {
            durability.DurabilityChanged += HandleDurabilityChanged;
            durability.Destroyed += HandleDestroyed;
        }
    }

    private void OnDisable()
    {
        if (durability != null)
        {
            durability.DurabilityChanged -= HandleDurabilityChanged;
            durability.Destroyed -= HandleDestroyed;
        }

        if (_attachedPuller != null)
        {
            CartPuller activePuller = _attachedPuller;
            CharacterController playerCollider = activePuller.GetComponent<CharacterController>();
            SetPlayerCollisionIgnored(playerCollider, false);
            _attachedPuller = null;
            activePuller.NotifyCartUnavailable(this);
        }

        _connectedAnchorBody = null;
        ResetTowMetrics();
        SetJointActive(false, null);
    }

    private void FixedUpdate()
    {
        UpdateCargoMassContribution();
        UpdateTowMetrics();
        ApplyTransportForces();
        ApplyStabilityAssist();

        if (_connectedAnchorBody == null || handleTransform == null)
        {
            return;
        }

        float effectiveMaxStretch = GetEffectiveMaxAttachmentStretch();
        if (_currentHandleStretch > effectiveMaxStretch)
        {
            if (_currentHandleStretch > maxAttachmentStretch)
            {
                float overloadDamage = (_currentHandleStretch - maxAttachmentStretch) * 8f;
                durability?.ApplyDamage(overloadDamage, handleTransform.position, Vector3.zero, this);
            }

            _attachedPuller?.DetachCurrentCart();
        }
    }

    public bool CanInteract(PlayerInteractor interactor)
    {
        if (interactor == null || IsBroken)
        {
            return false;
        }

        PlayerCarryController carryController = interactor.CarryController;
        if (carryController != null && carryController.IsCarrying)
        {
            return cargoGridInput != null && cargoGridInput.CanOpenFor(interactor);
        }

        CartPuller puller = interactor.GetComponent<CartPuller>();
        if (puller != null && _attachedPuller == puller)
        {
            return true;
        }

        float interactionRadius = Mathf.Max(handleAttachRadius, handleInteractRadius);
        return IsActorNearHandle(interactor, interactionRadius, handleVerticalTolerance);
    }

    public void Interact(PlayerInteractor interactor)
    {
        if (interactor == null)
        {
            return;
        }

        PlayerCarryController carryController = interactor.CarryController;
        if (carryController != null && carryController.IsCarrying)
        {
            if (cargoGridInput != null && cargoGridInput.TryOpenFromInteraction(interactor))
            {
                return;
            }

            return;
        }

        CartPuller puller = interactor.GetComponent<CartPuller>();
        if (puller == null)
        {
            return;
        }

        if (_attachedPuller == puller)
        {
            puller.DetachCurrentCart();
        }
        else
        {
            puller.AttachCart(this);
        }
    }

    public string GetInteractionLabel(PlayerInteractor interactor)
    {
        if (interactor != null && interactor.CarryController != null && interactor.CarryController.IsCarrying)
        {
            if (cargoGridInput != null && cargoGridInput.CanOpenFor(interactor))
            {
                return "Open cargo grid";
            }

            return "Move cargo closer to cart";
        }

        return _attachedPuller == null ? "Grab cart handle" : "Release cart";
    }

    public bool AttachToPuller(CartPuller puller, Rigidbody anchorBody, CharacterController playerCollider)
    {
        if (puller == null || anchorBody == null || IsBroken)
        {
            return false;
        }

        if (!IsActorNearHandle(puller, handleAttachRadius, handleVerticalTolerance))
        {
            return false;
        }

        if (!IsActorOnHandlePullSide(puller, rigidTowAllowedError + 0.05f))
        {
            return false;
        }

        if (_attachedPuller != null && _attachedPuller != puller)
        {
            _attachedPuller.DetachCurrentCart();
        }

        _attachedPuller = puller;
        _connectedAnchorBody = anchorBody;
        _hasLastAnchorPosition = false;
        SetJointActive(true, anchorBody);
        _lastAnchorPosition = GetTowConnectionTargetPosition();
        UpdateTowMetrics();
        SetPlayerCollisionIgnored(playerCollider, true);
        return true;
    }

    public void DetachFromPuller(CartPuller puller)
    {
        if (_attachedPuller != puller)
        {
            return;
        }

        SetPlayerCollisionIgnored(GetPlayerCollider(), false);
        _attachedPuller = null;
        _connectedAnchorBody = null;
        ResetTowMetrics();
        SetJointActive(false, null);
    }

    public float EvaluateSpeedMultiplier(Vector3 moveDirection, Vector3 groundNormal, bool sprinting)
    {
        if (_attachedPuller == null || moveDirection.sqrMagnitude < 0.001f)
        {
            return 1f;
        }

        UpdateTowStretch();

        float loadRatio = CalculateLoadRatio();
        float stabilityRisk = Mathf.Max(_cargoStabilityRisk, CalculateCargoStabilityRisk(CalculateDynamicCenterOfMass(baseCartMass)));
        _currentLoadRatio = loadRatio;
        _cargoStabilityRisk = stabilityRisk;
        float speedMultiplier = Mathf.Lerp(emptyCartSpeedMultiplier, minimumSpeedMultiplier, loadRatio);

        float slopeAmount = Mathf.Clamp01(1f - Vector3.Dot(groundNormal.normalized, Vector3.up));
        speedMultiplier -= slopeAmount * slopePenalty * Mathf.Lerp(0.35f, 1f, loadRatio);

        Vector3 moveDirectionNormalized = moveDirection.normalized;
        float alignmentPenalty = 1f - Mathf.Abs(Vector3.Dot(transform.forward.normalized, moveDirectionNormalized));
        speedMultiplier -= alignmentPenalty
            * turnPenalty
            * Mathf.Lerp(0.5f, 1.35f, loadRatio)
            * Mathf.Lerp(1f, 1.45f, stabilityRisk);
        speedMultiplier -= alignmentPenalty
            * sidePullSpeedPenalty
            * Mathf.Lerp(0.45f, 1.25f, loadRatio)
            * Mathf.Lerp(1f, 1.3f, stabilityRisk);
        speedMultiplier -= _towStrain01 * towStrainPenalty * Mathf.Lerp(0.35f, 1f, loadRatio);
        speedMultiplier -= stabilityRisk * cargoStabilitySpeedPenalty;

        if (sprinting)
        {
            speedMultiplier -= sprintPenalty * Mathf.Lerp(0.5f, 1f, loadRatio);
        }

        if (durability != null)
        {
            float damageRatio = 1f - durability.NormalizedDurability;
            speedMultiplier -= damageRatio * damagePenalty;
        }

        return Mathf.Clamp(speedMultiplier, minimumSpeedMultiplier * 0.8f, 1f);
    }

    private void UpdateTowMetrics()
    {
        if (_connectedAnchorBody == null || handleTransform == null)
        {
            ResetTowMetrics();
            return;
        }

        UpdateTowStretch();

        Vector3 anchorPosition = GetTowConnectionTargetPosition();
        if (_hasLastAnchorPosition)
        {
            Vector3 anchorDelta = Vector3.ProjectOnPlane(anchorPosition - _lastAnchorPosition, Vector3.up);
            _anchorPlanarVelocity = anchorDelta / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            _anchorPlanarSpeed = _anchorPlanarVelocity.magnitude;
        }
        else
        {
            _anchorPlanarVelocity = Vector3.zero;
            _anchorPlanarSpeed = 0f;
            _hasLastAnchorPosition = true;
        }

        _lastAnchorPosition = anchorPosition;
    }

    private void UpdateTowStretch()
    {
        if (_connectedAnchorBody == null || handleTransform == null)
        {
            _currentHandleStretch = 0f;
            _towStrain01 = 0f;
            return;
        }

        _currentHandleStretch = GetPlanarTowStretch(GetTowConnectionTargetPosition());
        _towStrain01 = Mathf.InverseLerp(
            Mathf.Max(0.001f, rigidTowAllowedError),
            GetEffectiveMaxAttachmentStretch(),
            _currentHandleStretch);
    }

    private void ResetTowMetrics()
    {
        _currentHandleStretch = 0f;
        _anchorPlanarSpeed = 0f;
        _anchorPlanarVelocity = Vector3.zero;
        _towStrain01 = 0f;
        _hasLastAnchorPosition = false;
    }

    private void ApplyTransportForces()
    {
        if (cartRigidbody == null)
        {
            return;
        }

        ApplyTowMotor();

        Vector3 planarVelocity = Vector3.ProjectOnPlane(cartRigidbody.linearVelocity, Vector3.up);
        float planarSpeed = planarVelocity.magnitude;
        if (planarSpeed > 0.01f)
        {
            Vector3 right = GetPlanarDirection(transform.right, Vector3.right);
            float lateralSpeed = Vector3.Dot(planarVelocity, right);
            float gripScale = Mathf.Lerp(1f, 0.65f, _cargoStabilityRisk) * Mathf.Lerp(0.85f, 1.15f, _currentLoadRatio);

            float lateralCorrection = 1f - Mathf.Exp(-lateralVelocityCorrection * gripScale * Time.fixedDeltaTime);
            if (lateralCorrection > 0f && Mathf.Abs(lateralSpeed) > 0.001f)
            {
                Vector3 verticalVelocity = Vector3.Project(cartRigidbody.linearVelocity, Vector3.up);
                planarVelocity -= right * (lateralSpeed * lateralCorrection);
                cartRigidbody.linearVelocity = planarVelocity + verticalVelocity;
                planarSpeed = planarVelocity.magnitude;
                lateralSpeed = Vector3.Dot(planarVelocity, right);
            }

            cartRigidbody.AddForce(-right * lateralSpeed * lateralGrip * gripScale, ForceMode.Acceleration);

            float resistance = rollingResistance * Mathf.Lerp(0.8f, 1.35f, _currentLoadRatio);
            cartRigidbody.AddForce(-planarVelocity.normalized * resistance, ForceMode.Acceleration);

            if (_connectedAnchorBody != null && _anchorPlanarSpeed <= stoppedAnchorBrakeSpeed && _towStrain01 < 0.25f)
            {
                cartRigidbody.AddForce(-planarVelocity * brakeDamping, ForceMode.Acceleration);

                if (planarSpeed <= stoppedVelocitySnap)
                {
                    cartRigidbody.linearVelocity = Vector3.Project(cartRigidbody.linearVelocity, Vector3.up);
                    planarVelocity = Vector3.zero;
                    planarSpeed = 0f;
                }
            }
        }

        ApplyYawAlignment(planarVelocity, planarSpeed);
    }

    private void ApplyTowMotor()
    {
        if (_connectedAnchorBody == null || handleTransform == null || cartRigidbody == null)
        {
            return;
        }

        if (pullJoint != null && pullJoint.connectedBody != null)
        {
            return;
        }

        Vector3 handlePosition = HandlePosition;
        Vector3 towTarget = GetTowConnectionTargetPosition();
        Vector3 handleError = Vector3.ProjectOnPlane(towTarget - handlePosition, Vector3.up);
        if (handleError.sqrMagnitude <= towMotorDeadZone * towMotorDeadZone)
        {
            return;
        }

        Vector3 handleVelocity = Vector3.ProjectOnPlane(cartRigidbody.GetPointVelocity(handlePosition), Vector3.up);
        Vector3 relativeVelocity = _anchorPlanarVelocity - handleVelocity;
        Vector3 acceleration = handleError * towMotorSpring + relativeVelocity * towMotorDamper;
        acceleration = Vector3.ClampMagnitude(acceleration, towMotorMaxAcceleration);
        cartRigidbody.AddForce(acceleration, ForceMode.Acceleration);

        Vector3 right = GetPlanarDirection(transform.right, Vector3.right);
        float lateralError = Vector3.Dot(handleError, right);
        float steeringFactor = Mathf.Clamp01(handleSteeringForceFactor);
        if (steeringFactor <= 0f)
        {
            return;
        }

        float steeringError01 = Mathf.Clamp(
            lateralError / Mathf.Max(0.01f, MovementTowSlack),
            -1f,
            1f);
        if (Mathf.Abs(steeringError01) <= 0.0001f)
        {
            return;
        }

        float steeringTorque = steeringError01 * yawAlignmentTorque * steeringFactor;
        float yawVelocity = Vector3.Dot(cartRigidbody.angularVelocity, Vector3.up);
        float steeringDamping = yawVelocity * yawAlignmentDamping * steeringFactor * 0.35f;
        cartRigidbody.AddTorque(Vector3.up * (steeringTorque - steeringDamping), ForceMode.Acceleration);
    }

    private void ApplyYawAlignment(Vector3 planarVelocity, float planarSpeed)
    {
        if (cartRigidbody == null)
        {
            return;
        }

        Vector3 forward = GetPlanarDirection(transform.forward, Vector3.forward);
        Vector3 desiredForward = Vector3.zero;

        if (_connectedAnchorBody != null)
        {
            Vector3 towDirection = Vector3.ProjectOnPlane(GetTowConnectionTargetPosition() - transform.position, Vector3.up);
            if (towDirection.sqrMagnitude > 0.001f)
            {
                desiredForward = towDirection.normalized;
                if (Vector3.Dot(forward, desiredForward) < 0f)
                {
                    desiredForward = -desiredForward;
                }
            }
        }

        if (desiredForward == Vector3.zero && planarSpeed > 0.35f)
        {
            desiredForward = planarVelocity.normalized;
            if (Vector3.Dot(forward, desiredForward) < 0f)
            {
                desiredForward = -desiredForward;
            }
        }

        if (desiredForward == Vector3.zero)
        {
            return;
        }

        float signedAngle = Vector3.SignedAngle(forward, desiredForward, Vector3.up);
        float responseAngle = _connectedAnchorBody != null ? 25f : 45f;
        float normalizedAngle = Mathf.Clamp(signedAngle / responseAngle, -1f, 1f);
        float torqueScale = Mathf.Lerp(0.75f, 1.25f, _currentLoadRatio) * Mathf.Lerp(1f, 0.55f, _cargoStabilityRisk);
        float yawAssist = normalizedAngle * yawAlignmentTorque * torqueScale;
        if (_connectedAnchorBody != null)
        {
            yawAssist *= Mathf.Max(0f, attachedYawAlignmentAssist);
        }

        float yawVelocity = Vector3.Dot(cartRigidbody.angularVelocity, Vector3.up);
        float yawDampingScale = 1f;
        if (_connectedAnchorBody != null)
        {
            yawDampingScale = Mathf.Lerp(0.35f, 1f, 1f - Mathf.Abs(normalizedAngle));
        }

        float yawDamping = yawVelocity
            * yawAlignmentDamping
            * yawDampingScale
            * Mathf.Lerp(0.9f, 1.2f, _currentLoadRatio);
        cartRigidbody.AddTorque(Vector3.up * (yawAssist - yawDamping), ForceMode.Acceleration);
    }

    private void ApplyStabilityAssist()
    {
        if (cartRigidbody == null)
        {
            return;
        }

        float stabilityMultiplier = 1f;
        if (durability != null)
        {
            if (durability.IsDestroyed)
            {
                stabilityMultiplier = destroyedStabilityMultiplier;
            }
            else if (durability.IsCritical)
            {
                stabilityMultiplier = criticalStabilityMultiplier;
            }
        }

        Vector3 correctionAxis = Vector3.Cross(transform.up, Vector3.up);
        if (correctionAxis.sqrMagnitude > 0.0001f)
        {
            cartRigidbody.AddTorque(correctionAxis * (uprightTorque * stabilityMultiplier), ForceMode.Acceleration);
        }

        cartRigidbody.angularDamping = Mathf.Lerp(stableAngularDamping * 0.45f, stableAngularDamping, stabilityMultiplier);
    }

    private void UpdateCargoMassContribution()
    {
        if (cartRigidbody == null)
        {
            return;
        }

        float cargoMass = Mathf.Max(0f, CurrentCargoMass);
        cartRigidbody.mass = baseCartMass + cargoMass;
        Vector3 dynamicCenterOfMass = CalculateDynamicCenterOfMass(baseCartMass);
        cartRigidbody.centerOfMass = dynamicCenterOfMass;
        _currentLoadRatio = CalculateLoadRatio();
        _cargoStabilityRisk = CalculateCargoStabilityRisk(dynamicCenterOfMass);
    }

    private float CalculateLoadRatio()
    {
        float pullMass = Mathf.Max(0f, baseCartMass) + Mathf.Max(0f, CurrentCargoMass);
        return Mathf.Clamp01(pullMass / Mathf.Max(1f, loadForMinimumSpeed));
    }

    private float CalculateCargoStabilityRisk(Vector3 localCenterOfMass)
    {
        float cargoMass = Mathf.Max(0f, CurrentCargoMass);
        if (cargoMass <= 0.001f)
        {
            return 0f;
        }

        Vector3 offsetFromBase = localCenterOfMass - centerOfMassOffset;
        float horizontalOffset = new Vector2(offsetFromBase.x, offsetFromBase.z).magnitude;
        float raisedCenter = Mathf.Max(0f, offsetFromBase.y);
        float cargoLoadRatio = Mathf.Clamp01(cargoMass / Mathf.Max(1f, loadForMinimumSpeed));
        return Mathf.Clamp01((horizontalOffset * 0.9f) + (raisedCenter * 0.35f) + (cargoLoadRatio * 0.15f));
    }

    private Vector3 CalculateDynamicCenterOfMass(float baseMass)
    {
        if (cartInventory == null)
        {
            return centerOfMassOffset;
        }

        List<CargoInstance> cargos = cartInventory.GetLoadedCargos();
        if (cargos.Count == 0)
        {
            return centerOfMassOffset;
        }

        Vector3 weightedCenter = centerOfMassOffset * Mathf.Max(0.01f, baseMass);
        float totalMass = Mathf.Max(0.01f, baseMass);

        for (int i = 0; i < cargos.Count; i++)
        {
            CargoInstance cargo = cargos[i];
            if (cargo == null)
            {
                continue;
            }

            Vector3 cargoCenter = cargo.ItemRigidbody != null
                ? cargo.ItemRigidbody.worldCenterOfMass
                : cargo.transform.position;

            weightedCenter += transform.InverseTransformPoint(cargoCenter) * cargo.PhysicalMass;
            totalMass += cargo.PhysicalMass;
        }

        return weightedCenter / Mathf.Max(0.01f, totalMass);
    }

    private bool IsPointNearHandle(Vector3 worldPoint, float planarRadius, float verticalTolerance)
    {
        Vector3 handlePosition = HandlePosition;
        float verticalDistance = Mathf.Abs(worldPoint.y - handlePosition.y);
        if (verticalDistance > verticalTolerance)
        {
            return false;
        }

        Vector3 planarOffset = Vector3.ProjectOnPlane(worldPoint - handlePosition, Vector3.up);
        return planarOffset.sqrMagnitude <= planarRadius * planarRadius;
    }

    private bool IsActorNearHandle(Component actor, float planarRadius, float verticalTolerance)
    {
        if (actor == null)
        {
            return false;
        }

        Vector3 handlePosition = HandlePosition;
        Vector3 samplePoint = GetClosestActorPoint(actor, handlePosition);
        return IsPointNearHandle(samplePoint, planarRadius, verticalTolerance);
    }

    private bool IsActorOnHandlePullSide(Component actor, float tolerance)
    {
        if (actor == null)
        {
            return false;
        }

        Vector3 handlePosition = HandlePosition;
        Vector3 samplePoint = GetActorSideCheckPoint(actor);
        Vector3 planarOffset = Vector3.ProjectOnPlane(samplePoint - handlePosition, Vector3.up);
        if (planarOffset.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        Vector3 pullDirection = GetHandlePullDirection();
        return Vector3.Dot(planarOffset, pullDirection) >= -Mathf.Max(0f, tolerance);
    }

    private static Vector3 GetActorSideCheckPoint(Component actor)
    {
        if (actor == null)
        {
            return Vector3.zero;
        }

        if (actor.TryGetComponent(out CharacterController characterController))
        {
            Bounds bounds = characterController.bounds;
            if (bounds.extents.sqrMagnitude > 0.0001f)
            {
                return bounds.center;
            }
        }

        if (actor.TryGetComponent(out Collider collider))
        {
            Bounds bounds = collider.bounds;
            if (bounds.extents.sqrMagnitude > 0.0001f)
            {
                return bounds.center;
            }
        }

        return actor.transform.position;
    }

    private static Vector3 GetClosestActorPoint(Component actor, Vector3 worldPoint)
    {
        if (actor == null)
        {
            return worldPoint;
        }

        if (actor.TryGetComponent(out CharacterController characterController))
        {
            Bounds bounds = characterController.bounds;
            if (bounds.extents.sqrMagnitude > 0.0001f)
            {
                return bounds.ClosestPoint(worldPoint);
            }
        }

        if (actor.TryGetComponent(out Collider collider))
        {
            return collider.ClosestPoint(worldPoint);
        }

        return actor.transform.position;
    }

    private float GetEffectiveMaxAttachmentStretch()
    {
        float jointLimit = Mathf.Max(0.001f, rigidTowAllowedError);
        float configuredLimit = Mathf.Min(
            Mathf.Max(jointLimit + 0.05f, maxAttachmentStretch),
            Mathf.Max(jointLimit + 0.05f, handleDetachDistance));
        return Mathf.Max(jointLimit + 0.01f, configuredLimit);
    }

    public float GetPlanarTowStretch(Vector3 towTargetPosition)
    {
        if (handleTransform == null)
        {
            return 0f;
        }

        Vector3 planarOffset = Vector3.ProjectOnPlane(HandlePosition - towTargetPosition, Vector3.up);
        return planarOffset.magnitude;
    }

    private static Vector3 GetPlanarDirection(Vector3 direction, Vector3 fallback)
    {
        Vector3 planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (planarDirection.sqrMagnitude <= 0.0001f)
        {
            planarDirection = Vector3.ProjectOnPlane(fallback, Vector3.up);
        }

        return planarDirection.sqrMagnitude > 0.0001f ? planarDirection.normalized : Vector3.forward;
    }

    private Vector3 GetHandlePullDirection()
    {
        if (handleTransform == null)
        {
            return GetPlanarDirection(-transform.forward, Vector3.back);
        }

        Vector3 awayFromCart = Vector3.ProjectOnPlane(handleTransform.position - transform.position, Vector3.up);
        if (awayFromCart.sqrMagnitude <= 0.0001f)
        {
            awayFromCart = Vector3.ProjectOnPlane(-transform.forward, Vector3.up);
        }

        return awayFromCart.sqrMagnitude > 0.0001f ? awayFromCart.normalized : Vector3.back;
    }

    private Vector3 GetTowConnectionTargetPosition()
    {
        if (_connectedAnchorBody == null)
        {
            return HandlePosition;
        }

        SpringJoint activePullJoint = ResolvePullJoint(false);
        Vector3 connectedAnchor = activePullJoint != null
            ? activePullJoint.connectedAnchor
            : GetConnectedAnchorOffset();
        return _connectedAnchorBody.transform.TransformPoint(connectedAnchor);
    }

    private Vector3 GetHandleLocalAnchor()
    {
        return handleTransform != null
            ? transform.InverseTransformPoint(GetHandleGripPosition())
            : Vector3.zero;
    }

    private Vector3 GetHandleGripPosition()
    {
        if (handleTransform == null)
        {
            return transform.position;
        }

        Vector3 awayFromCart = Vector3.ProjectOnPlane(handleTransform.position - transform.position, Vector3.up);
        if (awayFromCart.sqrMagnitude <= 0.0001f)
        {
            awayFromCart = Vector3.ProjectOnPlane(handleTransform.forward, Vector3.up);
        }

        if (_handleBoxCollider != null && awayFromCart.sqrMagnitude > 0.0001f)
        {
            Vector3 awayLocal = handleTransform.InverseTransformDirection(awayFromCart.normalized);
            Vector3 extents = _handleBoxCollider.size * 0.5f;
            Vector3 localGripPoint = _handleBoxCollider.center;
            Vector3 absAwayLocal = new Vector3(
                Mathf.Abs(awayLocal.x),
                Mathf.Abs(awayLocal.y),
                Mathf.Abs(awayLocal.z));

            if (absAwayLocal.x >= absAwayLocal.y && absAwayLocal.x >= absAwayLocal.z)
            {
                localGripPoint.x += Mathf.Sign(awayLocal.x) * extents.x;
            }
            else if (absAwayLocal.y >= absAwayLocal.z)
            {
                localGripPoint.y += Mathf.Sign(awayLocal.y) * extents.y;
            }
            else
            {
                localGripPoint.z += Mathf.Sign(awayLocal.z) * extents.z;
            }

            return handleTransform.TransformPoint(localGripPoint)
                + (awayFromCart.normalized * handleGripSurfaceOffset);
        }

        return handleTransform.TransformPoint(handleGripLocalOffset);
    }

    private Vector3 GetConnectedAnchorOffset()
    {
        return Vector3.back * Mathf.Max(0f, rigidTowBehindOffset);
    }

    private void DestroyLegacyConfigurableJoint()
    {
        ConfigurableJoint legacyJoint = GetComponent<ConfigurableJoint>();
        if (legacyJoint == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(legacyJoint);
        }
        else
        {
            DestroyImmediate(legacyJoint);
        }
    }

    private void ConfigureJointDefaults()
    {
        SpringJoint activePullJoint = ResolvePullJoint(true);
        if (activePullJoint == null)
        {
            return;
        }

        activePullJoint.autoConfigureConnectedAnchor = false;
        activePullJoint.enableCollision = false;
        activePullJoint.enablePreprocessing = true;
        activePullJoint.anchor = GetHandleLocalAnchor();
        activePullJoint.connectedAnchor = GetConnectedAnchorOffset();
        activePullJoint.spring = towJointSpring;
        activePullJoint.damper = towJointDamper;
        activePullJoint.minDistance = towJointMinDistance;
        activePullJoint.maxDistance = towJointMaxDistance;
        activePullJoint.tolerance = towJointTolerance;
        activePullJoint.massScale = Mathf.Max(0.01f, towJointMassScale);
        activePullJoint.connectedMassScale = Mathf.Max(0.01f, towJointConnectedMassScale);

        _defaultJointConnectedAnchor = activePullJoint.connectedAnchor;
        _defaultJointMassScale = activePullJoint.massScale;
        _defaultJointConnectedMassScale = activePullJoint.connectedMassScale;

        SetJointActive(false, null);
    }

    private void HandleDurabilityChanged(Durability currentDurability)
    {
        UpdateDamageTint();
    }

    private void HandleDestroyed(Durability currentDurability)
    {
        UpdateDamageTint();

        if (_attachedPuller != null)
        {
            CartPuller activePuller = _attachedPuller;
            activePuller.DetachCurrentCart();
            activePuller.NotifyCartUnavailable(this);
        }

        SetJointActive(false, null);
    }

    private void SetJointActive(bool active, Rigidbody anchorBody)
    {
        try
        {
            SpringJoint activePullJoint = ResolvePullJoint(active);
            if (activePullJoint == null)
            {
                return;
            }

            // Физический SpringJoint выключен: он прикладывал силу в точке
            // ручки (выше центра масс) и при жёсткой пружине создавал опрокидывающий
            // момент, из-за которого телегу подбрасывало и вдавливало в землю.
            // Тяга теперь идёт через ApplyTowMotor(): силу прикладываем в центре
            // масс и строго в горизонтальной плоскости. connectedAnchor всё равно
            // сохраняем в корректном значении — GetTowConnectionTargetPosition()
            // читает его, чтобы вычислить, куда мотор тянет ручку.
            _defaultJointConnectedAnchor = GetConnectedAnchorOffset();
            activePullJoint.anchor = GetHandleLocalAnchor();
            activePullJoint.connectedBody = null;
            activePullJoint.connectedAnchor = _defaultJointConnectedAnchor;
            activePullJoint.spring = 0f;
            activePullJoint.damper = 0f;
            activePullJoint.minDistance = 0f;
            activePullJoint.maxDistance = 0f;
            activePullJoint.tolerance = 0f;
            activePullJoint.massScale = _defaultJointMassScale;
            activePullJoint.connectedMassScale = _defaultJointConnectedMassScale;
        }
        catch (MissingReferenceException)
        {
            pullJoint = null;
        }
    }

    private SpringJoint ResolvePullJoint(bool createIfMissing)
    {
        SpringJoint resolvedPullJoint = GetComponent<SpringJoint>();
        if (resolvedPullJoint == null && createIfMissing)
        {
            resolvedPullJoint = gameObject.AddComponent<SpringJoint>();
        }

        pullJoint = resolvedPullJoint;
        return resolvedPullJoint;
    }

    private void UpdateDamageTint()
    {
        Color tint = healthyColor;
        if (durability != null)
        {
            tint = durability.IsDestroyed
                ? destroyedColor
                : Color.Lerp(damagedColor, healthyColor, durability.NormalizedDurability);
        }

        for (int i = 0; i < cartRenderers.Length; i++)
        {
            ApplyTint(cartRenderers[i], tint);
        }
    }

    private CharacterController GetPlayerCollider()
    {
        return _attachedPuller != null ? _attachedPuller.GetComponent<CharacterController>() : null;
    }

    private void SetPlayerCollisionIgnored(CharacterController playerCollider, bool ignored)
    {
        if (playerCollider == null || cartColliders == null)
        {
            return;
        }

        for (int i = 0; i < cartColliders.Length; i++)
        {
            Collider cartCollider = cartColliders[i];
            if (cartCollider == null)
            {
                continue;
            }

            Physics.IgnoreCollision(playerCollider, cartCollider, ignored);
        }
    }

    private static void ApplyTint(Renderer targetRenderer, Color color)
    {
        if (targetRenderer == null)
        {
            return;
        }

        Material material = targetRenderer.material;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

}
