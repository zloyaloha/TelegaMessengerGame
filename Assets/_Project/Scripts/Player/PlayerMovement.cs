using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Движение")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float jumpForce = 5f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float groundedVerticalVelocity = -2f;

    [Header("Камера")]
    [SerializeField] private Transform cameraTransform;

    [Header("Анимации")]
    [SerializeField] private Animator animator;

    [Header("Внешняя Нагрузка")]
    [SerializeField] private CartPuller cartPuller;
    [SerializeField] private CartController spawnCompanionCart;
    [SerializeField] private CargoInstance[] spawnCompanionCargo;
    [SerializeField, Min(0f)] private float spawnImpactGracePeriod = 1.5f;

    [Header("Прыжок И Земля")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float coyoteTime = 0.15f;
    [SerializeField] private float jumpBufferTime = 0.12f;
    [SerializeField] private float jumpAnimationLeadTime = 0.08f;
    [SerializeField] private float landingAnimationHoldTime = 0.12f;
    [SerializeField] private float groundCheckDistance = 0.2f;
    [SerializeField] private float minGroundNormal = 0.65f;
    [SerializeField] private float groundedProbeOffset = 0.05f;
    [SerializeField] private float animationDampTime = 0.08f;

    [Header("Случайный Спавн")]
    [SerializeField] private bool randomizeSpawnOnStart = true;
    [SerializeField, Range(0.1f, 1f)] private float maxSpawnHeightNormalized = 0.72f;
    [SerializeField, Min(0f)] private float spawnEdgePadding = 24f;
    [SerializeField, Min(1)] private int spawnAttemptsPerFrame = 10;
    [SerializeField, Min(1)] private int spawnRetryFrames = 90;
    [SerializeField, Min(10f)] private float spawnProbeHeight = 600f;
    [SerializeField, Min(0.05f)] private float spawnClearance = 0.2f;

    private CharacterController _characterController;
    private Keyboard _keyboard;
    private readonly RaycastHit[] _groundHits = new RaycastHit[8];
    private WorldGenerator _worldGenerator;

    private Vector3 _moveDirection;
    private float _verticalVelocity;
    private float _coyoteCounter;
    private float _jumpBufferCounter;
    private float _jumpLeadTimer;
    private float _landingHoldCounter;
    private bool _isGrounded;
    private bool _jumpQueued;
    private bool _isJumpingAnimationActive;
    private Vector3 _lastGroundNormal = Vector3.up;
    private bool _awaitingSurfaceSpawn;
    private int _remainingSpawnRetryFrames;
    private SpawnCompanionState[] _spawnCompanions = System.Array.Empty<SpawnCompanionState>();

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int IsJumpingHash = Animator.StringToHash("IsJumping");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int VerticalVelocityHash = Animator.StringToHash("VerticalVelocity");

    private struct SpawnCompanionState
    {
        public Transform Transform;
        public Rigidbody Rigidbody;
        public ImpactDamageReceiver ImpactDamageReceiver;
        public Vector3 PositionOffset;
        public Quaternion RotationOffset;
    }

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _keyboard = Keyboard.current;
        cartPuller = cartPuller != null ? cartPuller : GetComponent<CartPuller>();
        spawnCompanionCart = spawnCompanionCart != null ? spawnCompanionCart : FindFirstObjectByType<CartController>();
        if (spawnCompanionCargo == null || spawnCompanionCargo.Length == 0)
        {
            spawnCompanionCargo = FindObjectsByType<CargoInstance>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        _worldGenerator = FindFirstObjectByType<WorldGenerator>();
        CacheSpawnCompanions();
    }

    public void SetRandomSpawnOnStart(bool enabled)
    {
        randomizeSpawnOnStart = enabled;
        if (!enabled)
        {
            _awaitingSurfaceSpawn = false;
            _spawnCompanions = System.Array.Empty<SpawnCompanionState>();
        }
    }

    public void TeleportTo(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (_characterController == null)
        {
            transform.SetPositionAndRotation(worldPosition, worldRotation);
            return;
        }

        bool controllerWasEnabled = _characterController.enabled;
        if (controllerWasEnabled)
        {
            _characterController.enabled = false;
        }

        transform.SetPositionAndRotation(worldPosition, worldRotation);

        if (controllerWasEnabled)
        {
            _characterController.enabled = true;
        }

        ResetMotionState();
        InitializeGroundState();
    }

    public void TeleportToGround(Vector3 groundPoint, Quaternion worldRotation, float groundClearance = 0.02f)
    {
        if (_characterController == null)
        {
            TeleportTo(groundPoint + (Vector3.up * Mathf.Max(0f, groundClearance)), worldRotation);
            return;
        }

        Vector3 lossyScale = transform.lossyScale;
        Vector3 scaledCenter = Vector3.Scale(_characterController.center, lossyScale);
        float scaledRadius = _characterController.radius * Mathf.Max(lossyScale.x, lossyScale.z);
        float halfHeight = Mathf.Max(_characterController.height * lossyScale.y * 0.5f, scaledRadius);
        float clearance = Mathf.Max(0f, groundClearance);
        Vector3 worldPosition = groundPoint
            + (Vector3.up * (halfHeight + clearance))
            - (worldRotation * scaledCenter);

        TeleportTo(worldPosition, worldRotation);
    }

    private void Start()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (randomizeSpawnOnStart)
        {
            _remainingSpawnRetryFrames = Mathf.Max(1, spawnRetryFrames);
            _awaitingSurfaceSpawn = !TryPlaceOnRandomSurface();
        }

        if (!_awaitingSurfaceSpawn)
        {
            if (randomizeSpawnOnStart)
            {
                AlignCompanionCartToSpawn();
            }

            InitializeGroundState();
        }
    }

    private void Update()
    {
        if (_awaitingSurfaceSpawn)
        {
            TryResolveSurfaceSpawn();
            return;
        }

        if (_keyboard == null)
        {
            _keyboard = Keyboard.current;
            if (_keyboard == null)
            {
                return;
            }
        }

        ReadInput();
        UpdateJumpBuffer();
        SimulateMovement();
        RotateTowardsMovement();
        UpdateAnimations();
    }

    private void InitializeGroundState()
    {
        _isGrounded = ProbeGround();
        _coyoteCounter = _isGrounded ? coyoteTime : 0f;
        _verticalVelocity = groundedVerticalVelocity;
    }

    private void ResetMotionState()
    {
        _moveDirection = Vector3.zero;
        _jumpBufferCounter = 0f;
        _jumpLeadTimer = 0f;
        _landingHoldCounter = 0f;
        _jumpQueued = false;
        _isJumpingAnimationActive = false;
        _verticalVelocity = groundedVerticalVelocity;
    }

    private void TryResolveSurfaceSpawn()
    {
        if (TryPlaceOnRandomSurface())
        {
            _awaitingSurfaceSpawn = false;
            AlignCompanionCartToSpawn();
            InitializeGroundState();
            return;
        }

        _remainingSpawnRetryFrames--;
        if (_remainingSpawnRetryFrames > 0)
        {
            return;
        }

        _awaitingSurfaceSpawn = false;
        AlignCompanionCartToSpawn();
        InitializeGroundState();
    }

    private bool TryPlaceOnRandomSurface()
    {
        if (_characterController == null)
        {
            return false;
        }

        WorldGenerator worldGenerator = _worldGenerator != null ? _worldGenerator : FindFirstObjectByType<WorldGenerator>();
        WorldSettings worldSettings = worldGenerator != null ? worldGenerator.Settings : null;
        if (worldGenerator == null || worldSettings == null)
        {
            return false;
        }

        float worldWidth = worldSettings.worldSizeInChunks * (worldSettings.chunkWidth - 1) * worldSettings.meshScale;
        float worldDepth = worldSettings.worldSizeInChunks * (worldSettings.chunkHeight - 1) * worldSettings.meshScale;
        if (worldWidth <= 0.01f || worldDepth <= 0.01f)
        {
            return false;
        }

        Vector2 terrainHeightRange = worldSettings.EvaluateHeightRange(0f, 1f);
        float worldBaseY = worldGenerator.transform.position.y;
        float maxAllowedSpawnY = worldBaseY + Mathf.Lerp(terrainHeightRange.x, terrainHeightRange.y, maxSpawnHeightNormalized);
        float rayStartY = worldBaseY + terrainHeightRange.y + spawnProbeHeight;
        float rayDistance = Mathf.Max(spawnProbeHeight + Mathf.Abs(terrainHeightRange.y - terrainHeightRange.x) + 100f, 200f);
        float edgePadding = Mathf.Min(spawnEdgePadding, Mathf.Min(worldWidth, worldDepth) * 0.45f);
        float minX = worldGenerator.transform.position.x + edgePadding;
        float maxX = worldGenerator.transform.position.x + worldWidth - edgePadding;
        float minZ = worldGenerator.transform.position.z + edgePadding;
        float maxZ = worldGenerator.transform.position.z + worldDepth - edgePadding;

        if (minX >= maxX || minZ >= maxZ)
        {
            return false;
        }

        for (int attempt = 0; attempt < Mathf.Max(1, spawnAttemptsPerFrame); attempt++)
        {
            Vector3 rayOrigin = new Vector3(
                Random.Range(minX, maxX),
                rayStartY,
                Random.Range(minZ, maxZ));

            if (!TryFindSpawnHit(rayOrigin, rayDistance, out RaycastHit hit))
            {
                continue;
            }

            if (hit.normal.y < minGroundNormal || hit.point.y > maxAllowedSpawnY)
            {
                continue;
            }

            TeleportToSurfacePoint(hit.point);
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            AlignCompanionCartToSpawn();
            return true;
        }

        return false;
    }

    private bool TryFindSpawnHit(Vector3 rayOrigin, float rayDistance, out RaycastHit hit)
    {
        if (Physics.Raycast(rayOrigin, Vector3.down, out hit, rayDistance, groundLayer, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return Physics.Raycast(rayOrigin, Vector3.down, out hit, rayDistance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
    }

    private void TeleportToSurfacePoint(Vector3 surfacePoint)
    {
        float clearance = Mathf.Clamp(spawnClearance, 0.01f, 0.05f);
        TeleportToGround(surfacePoint, transform.rotation, clearance);
    }

    private void CacheSpawnCompanions()
    {
        List<SpawnCompanionState> companions = new List<SpawnCompanionState>();

        if (spawnCompanionCart != null)
        {
            companions.Add(CreateSpawnCompanionState(spawnCompanionCart.transform));
        }

        if (spawnCompanionCargo != null)
        {
            for (int i = 0; i < spawnCompanionCargo.Length; i++)
            {
                CargoInstance cargoItem = spawnCompanionCargo[i];
                if (cargoItem == null)
                {
                    continue;
                }

                companions.Add(CreateSpawnCompanionState(cargoItem.transform));
            }
        }

        _spawnCompanions = companions.ToArray();
    }

    private SpawnCompanionState CreateSpawnCompanionState(Transform targetTransform)
    {
        return new SpawnCompanionState
        {
            Transform = targetTransform,
            Rigidbody = targetTransform != null ? targetTransform.GetComponent<Rigidbody>() : null,
            ImpactDamageReceiver = targetTransform != null ? targetTransform.GetComponent<ImpactDamageReceiver>() : null,
            PositionOffset = Quaternion.Inverse(transform.rotation) * (targetTransform.position - transform.position),
            RotationOffset = Quaternion.Inverse(transform.rotation) * targetTransform.rotation
        };
    }

    private void AlignCompanionCartToSpawn()
    {
        for (int i = 0; i < _spawnCompanions.Length; i++)
        {
            SpawnCompanionState companion = _spawnCompanions[i];
            if (companion.Transform == null)
            {
                continue;
            }

            Vector3 targetPosition = transform.position + transform.rotation * companion.PositionOffset;
            Quaternion targetRotation = transform.rotation * companion.RotationOffset;

            if (companion.Rigidbody != null)
            {
                companion.Rigidbody.linearVelocity = Vector3.zero;
                companion.Rigidbody.angularVelocity = Vector3.zero;
            }

            companion.Transform.SetPositionAndRotation(targetPosition, targetRotation);

            if (companion.ImpactDamageReceiver != null)
            {
                companion.ImpactDamageReceiver.IgnoreImpactsForSeconds(spawnImpactGracePeriod);
            }
        }
    }

    private void ReadInput()
    {
        if (cameraTransform == null)
        {
            _moveDirection = Vector3.zero;
            return;
        }

        float horizontal = 0f;
        float vertical = 0f;

        if (_keyboard.dKey.isPressed) horizontal += 1f;
        if (_keyboard.aKey.isPressed) horizontal -= 1f;
        if (_keyboard.wKey.isPressed) vertical += 1f;
        if (_keyboard.sKey.isPressed) vertical -= 1f;

        Vector3 cameraForward = cameraTransform.forward;
        Vector3 cameraRight = cameraTransform.right;
        cameraForward.y = 0f;
        cameraRight.y = 0f;
        cameraForward.Normalize();
        cameraRight.Normalize();

        _moveDirection = (cameraForward * vertical + cameraRight * horizontal).normalized;
    }

    private void UpdateJumpBuffer()
    {
        if (_keyboard.spaceKey.wasPressedThisFrame)
        {
            _jumpBufferCounter = jumpBufferTime;

            if (CanStartJump())
            {
                QueueJumpSequence();
            }
        }
        else if (_jumpBufferCounter > 0f)
        {
            _jumpBufferCounter = Mathf.Max(0f, _jumpBufferCounter - Time.deltaTime);
        }
    }

    private void SimulateMovement()
    {
        bool groundedBeforeMove = ProbeGround();

        if (groundedBeforeMove)
        {
            _coyoteCounter = coyoteTime;

            if (_verticalVelocity < groundedVerticalVelocity)
            {
                _verticalVelocity = groundedVerticalVelocity;
            }
        }
        else
        {
            _coyoteCounter = Mathf.Max(0f, _coyoteCounter - Time.deltaTime);
        }

        if (!_jumpQueued && CanStartJump())
        {
            QueueJumpSequence();
        }

        bool startedJump = TryStartJump();

        if (!startedJump && !_jumpQueued)
        {
            _verticalVelocity += gravity * Time.deltaTime;
        }

        bool isSprinting = _keyboard.leftShiftKey.isPressed;
        float currentSpeed = isSprinting ? sprintSpeed : moveSpeed;
        if (cartPuller != null)
        {
            currentSpeed *= cartPuller.GetMovementSpeedMultiplier(_moveDirection, _lastGroundNormal, isSprinting);
        }

        Vector3 horizontalVelocity = _moveDirection * currentSpeed;
        Vector3 motion = (horizontalVelocity + Vector3.up * _verticalVelocity) * Time.deltaTime;
        if (cartPuller != null)
        {
            motion = cartPuller.ConstrainMotionToAttachedCart(motion);
        }

        CollisionFlags collisionFlags = _characterController.Move(motion);
        bool groundedAfterMove = (collisionFlags & CollisionFlags.Below) != 0 || ProbeGround();

        _isGrounded = groundedAfterMove;

        if (_isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = groundedVerticalVelocity;
            _coyoteCounter = coyoteTime;
        }

        UpdateJumpAnimationState();
    }

    private bool TryStartJump()
    {
        if (!_jumpQueued)
        {
            return false;
        }

        _jumpLeadTimer = Mathf.Max(0f, _jumpLeadTimer - Time.deltaTime);
        if (_jumpLeadTimer > 0f)
        {
            return false;
        }

        _jumpQueued = false;
        _isGrounded = false;
        _verticalVelocity = jumpForce;
        return true;
    }

    private bool CanStartJump()
    {
        return _jumpBufferCounter > 0f && _coyoteCounter > 0f;
    }

    private void QueueJumpSequence()
    {
        _jumpBufferCounter = 0f;
        _coyoteCounter = 0f;
        _jumpQueued = true;
        _jumpLeadTimer = jumpAnimationLeadTime;
        _landingHoldCounter = 0f;
        _isJumpingAnimationActive = true;
        TriggerJumpAnimation();
    }

    private void UpdateJumpAnimationState()
    {
        if (!_isJumpingAnimationActive)
        {
            return;
        }

        if (_jumpQueued || !_isGrounded)
        {
            _landingHoldCounter = 0f;
            return;
        }

        if (_landingHoldCounter <= 0f)
        {
            _landingHoldCounter = landingAnimationHoldTime;
            return;
        }

        _landingHoldCounter = Mathf.Max(0f, _landingHoldCounter - Time.deltaTime);
        if (_landingHoldCounter <= 0f)
        {
            _isJumpingAnimationActive = false;
        }
    }

    private void RotateTowardsMovement()
    {
        if (_moveDirection.sqrMagnitude < 0.01f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(_moveDirection);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime);
    }

    private bool ProbeGround()
    {
        if (_characterController == null)
        {
            return false;
        }

        if (_verticalVelocity > 0.1f)
        {
            return false;
        }

        Vector3 center = transform.TransformPoint(_characterController.center);
        float radius = _characterController.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) * 0.92f;
        float height = Mathf.Max(_characterController.height * transform.lossyScale.y, radius * 2f);
        float feetOffset = Mathf.Max(0f, (height * 0.5f) - radius);
        float castDistance = Mathf.Max(groundCheckDistance, groundedProbeOffset + _characterController.skinWidth);
        Vector3 probeOrigin = center + (Vector3.down * feetOffset) + (Vector3.up * groundedProbeOffset);

        if (TryFindGround(groundLayer, probeOrigin, radius, castDistance))
        {
            return true;
        }

        bool foundGround = TryFindGround(Physics.AllLayers, probeOrigin, radius, castDistance);
        if (!foundGround)
        {
            _lastGroundNormal = Vector3.up;
        }

        return foundGround;
    }

    private bool TryFindGround(LayerMask mask, Vector3 origin, float radius, float castDistance)
    {
        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            radius,
            Vector3.down,
            _groundHits,
            castDistance,
            mask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _groundHits[i];
            if (hit.collider == null)
            {
                continue;
            }

            if (hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.normal.y < minGroundNormal)
            {
                continue;
            }

            _lastGroundNormal = hit.normal;
            return true;
        }

        return false;
    }

    private void TriggerJumpAnimation()
    {
        if (animator == null)
        {
            return;
        }

        animator.ResetTrigger(JumpHash);
        animator.SetTrigger(JumpHash);
    }

    private void UpdateAnimations()
    {
        if (animator == null)
        {
            return;
        }

        Vector3 horizontalVelocity = _characterController.velocity;
        horizontalVelocity.y = 0f;

        animator.SetFloat(SpeedHash, horizontalVelocity.magnitude, animationDampTime, Time.deltaTime);
        animator.SetBool(IsJumpingHash, _isJumpingAnimationActive || _jumpQueued);
        animator.SetBool(IsGroundedHash, _isGrounded);
        animator.SetFloat(VerticalVelocityHash, _verticalVelocity);
    }

    private void OnDrawGizmosSelected()
    {
        CharacterController controller = _characterController != null ? _characterController : GetComponent<CharacterController>();
        if (controller == null)
        {
            return;
        }

        Vector3 center = transform.TransformPoint(controller.center);
        float radius = controller.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) * 0.92f;
        float height = Mathf.Max(controller.height * transform.lossyScale.y, radius * 2f);
        float feetOffset = Mathf.Max(0f, (height * 0.5f) - radius);
        float castDistance = Mathf.Max(groundCheckDistance, groundedProbeOffset + controller.skinWidth);
        Vector3 probeOrigin = center + (Vector3.down * feetOffset) + (Vector3.up * groundedProbeOffset);

        Gizmos.color = _isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(probeOrigin + (Vector3.down * castDistance), radius);
    }
}
