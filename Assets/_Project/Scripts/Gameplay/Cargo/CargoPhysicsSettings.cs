using UnityEngine;

[CreateAssetMenu(fileName = "CargoPhysicsSettings", menuName = "Gameplay/Cargo/Cargo Physics Settings")]
public class CargoPhysicsSettings : ScriptableObject
{
    private const string ResourceName = "CargoPhysicsSettings";

    [Header("Cargo Body")]
    // A small amount of linear drag makes crates settle like heavy wood on planks
    // instead of sliding forever after every bump. Lower it for slick cargo,
    // raise it if the load should calm down faster after braking or cornering.
    [SerializeField, Min(0f)] private float cargoDrag = 1.5f;

    // Angular drag prevents endless spinning after impacts. Lower values make cargo
    // wobble for longer, higher values make stacks feel more damped and stable.
    [SerializeField, Min(0f)] private float cargoAngularDrag = 2f;

    // Static friction keeps cargo from starting to slide on the cart floor.
    [SerializeField, Range(0f, 1f)] private float cargoFriction = 0.6f;

    // Dynamic friction slows cargo that is already sliding. Keep it close to
    // static friction for a grippy cart, lower it for unstable loads.
    [SerializeField, Range(0f, 1f)] private float cargoDynamicFriction = 0.5f;

    [Header("Cart Contact")]
    [SerializeField, Range(0f, 1f)] private float cartFriction = 0.8f;
    [SerializeField, Range(0f, 1f)] private float cartDynamicFriction = 0.7f;

    // A tiny bounce keeps impacts from feeling completely dead while avoiding the
    // "rubber box" look. Raise only a little if you want springier cargo.
    [SerializeField, Range(0f, 1f)] private float cargoBounciness = 0.05f;

    // This scales every cargo Rigidbody mass without touching authored data values.
    // Keep it at 1 for realistic tuning, lower it if the cart feels too sluggish,
    // or raise it if cargo should dominate handling and suspension response.
    [SerializeField, Min(0.01f)] private float cargoMassMultiplier = 1f;

    [Header("Falling Detection")]
    // We wait until a box drops clearly below the cart before marking it as lost.
    // A smaller threshold reacts earlier, a bigger one is more forgiving.
    [SerializeField, Min(0.1f)] private float fallDistanceThreshold = 2f;

    private static CargoPhysicsSettings _runtimeFallback;
    private PhysicsMaterial _runtimeCargoMaterial;
    private PhysicsMaterial _runtimeCartMaterial;

    public float CargoDrag => Mathf.Max(0f, cargoDrag);
    public float CargoAngularDrag => Mathf.Max(0f, cargoAngularDrag);
    public float CargoFriction => Mathf.Clamp01(cargoFriction);
    public float CargoDynamicFriction => Mathf.Clamp01(cargoDynamicFriction);
    public float CartFriction => Mathf.Clamp01(cartFriction);
    public float CartDynamicFriction => Mathf.Clamp01(cartDynamicFriction);
    public float CargoBounciness => Mathf.Clamp01(cargoBounciness);
    public float CargoMassMultiplier => Mathf.Max(0.01f, cargoMassMultiplier);
    public float FallDistanceThreshold => Mathf.Max(0.1f, fallDistanceThreshold);

    public static CargoPhysicsSettings Load()
    {
        CargoPhysicsSettings asset = Resources.Load<CargoPhysicsSettings>(ResourceName);
        if (asset != null)
        {
            return asset;
        }

        if (_runtimeFallback == null)
        {
            _runtimeFallback = CreateInstance<CargoPhysicsSettings>();
            _runtimeFallback.hideFlags = HideFlags.HideAndDontSave;
        }

        return _runtimeFallback;
    }

    public PhysicsMaterial GetOrCreateCargoMaterial()
    {
        if (_runtimeCargoMaterial == null)
        {
            _runtimeCargoMaterial = new PhysicsMaterial("RuntimeCargoMaterial");
            _runtimeCargoMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        _runtimeCargoMaterial.staticFriction = CargoFriction;
        _runtimeCargoMaterial.dynamicFriction = CargoDynamicFriction;
        _runtimeCargoMaterial.bounciness = CargoBounciness;
        _runtimeCargoMaterial.frictionCombine = PhysicsMaterialCombine.Maximum;
        _runtimeCargoMaterial.bounceCombine = PhysicsMaterialCombine.Minimum;
        return _runtimeCargoMaterial;
    }

    public PhysicsMaterial GetOrCreateCartMaterial()
    {
        if (_runtimeCartMaterial == null)
        {
            _runtimeCartMaterial = new PhysicsMaterial("RuntimeCartCargoContactMaterial");
            _runtimeCartMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        _runtimeCartMaterial.staticFriction = CartFriction;
        _runtimeCartMaterial.dynamicFriction = CartDynamicFriction;
        _runtimeCartMaterial.bounciness = 0f;
        _runtimeCartMaterial.frictionCombine = PhysicsMaterialCombine.Maximum;
        _runtimeCartMaterial.bounceCombine = PhysicsMaterialCombine.Minimum;
        return _runtimeCartMaterial;
    }

    private void OnValidate()
    {
        cargoDrag = Mathf.Max(0f, cargoDrag);
        cargoAngularDrag = Mathf.Max(0f, cargoAngularDrag);
        cargoFriction = Mathf.Clamp01(cargoFriction);
        cargoDynamicFriction = Mathf.Clamp01(cargoDynamicFriction);
        cartFriction = Mathf.Clamp01(cartFriction);
        cartDynamicFriction = Mathf.Clamp01(cartDynamicFriction);
        cargoBounciness = Mathf.Clamp01(cargoBounciness);
        cargoMassMultiplier = Mathf.Max(0.01f, cargoMassMultiplier);
        fallDistanceThreshold = Mathf.Max(0.1f, fallDistanceThreshold);

        if (_runtimeCargoMaterial != null)
        {
            _runtimeCargoMaterial.staticFriction = CargoFriction;
            _runtimeCargoMaterial.dynamicFriction = CargoDynamicFriction;
            _runtimeCargoMaterial.bounciness = CargoBounciness;
            _runtimeCargoMaterial.frictionCombine = PhysicsMaterialCombine.Maximum;
            _runtimeCargoMaterial.bounceCombine = PhysicsMaterialCombine.Minimum;
        }

        if (_runtimeCartMaterial != null)
        {
            _runtimeCartMaterial.staticFriction = CartFriction;
            _runtimeCartMaterial.dynamicFriction = CartDynamicFriction;
            _runtimeCartMaterial.bounciness = 0f;
            _runtimeCartMaterial.frictionCombine = PhysicsMaterialCombine.Maximum;
            _runtimeCartMaterial.bounceCombine = PhysicsMaterialCombine.Minimum;
        }
    }
}
