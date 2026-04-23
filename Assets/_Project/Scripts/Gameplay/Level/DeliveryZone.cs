using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class DeliveryZone : MonoBehaviour
{
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Transform lightBeamRoot;
    [SerializeField] private MeshRenderer visualRenderer;
    [SerializeField] private SphereCollider triggerCollider;
    [SerializeField, Min(0.5f)] private float deliveryRadius = 5f;
    [SerializeField, Min(1f)] private float visualHeight = 7f;
    [SerializeField, Min(0f)] private float rotationSpeed = 20f;
    [SerializeField] private bool showLightBeam = true;
    [SerializeField] private Color zoneColor = new Color(1f, 0.9f, 0f, 0.25f);
    [SerializeField] private Color beamColor = new Color(1f, 0.95f, 0.45f, 0.1f);

    private LevelManager _levelManager;
    private MeshRenderer _beamRenderer;

    private void Awake()
    {
        EnsureReferences();
        ApplyVisuals();
    }

    private void Update()
    {
        transform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.World);
    }

    public void Initialize(LevelManager owner, float radius)
    {
        _levelManager = owner;
        deliveryRadius = Mathf.Max(0.5f, radius);
        EnsureReferences();
        ApplyVisuals();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null)
        {
            return;
        }

        if (other.GetComponentInParent<CartInventory>() == null
            && other.GetComponentInParent<CartController>() == null)
        {
            return;
        }

        if (_levelManager == null)
        {
            _levelManager = FindFirstObjectByType<LevelManager>();
        }

        _levelManager?.OnDelivery();
    }

    private void OnValidate()
    {
        EnsureReferences();
        ApplyVisuals();
    }

    private void EnsureReferences()
    {
        if (visualRoot == null)
        {
            Transform child = transform.Find("VisualRoot");
            if (child != null)
            {
                visualRoot = child;
            }
        }

        if (lightBeamRoot == null)
        {
            Transform child = transform.Find("LightBeam");
            if (child != null)
            {
                lightBeamRoot = child;
            }
        }

        if (visualRenderer == null && visualRoot != null)
        {
            visualRenderer = visualRoot.GetComponent<MeshRenderer>();
        }

        if (triggerCollider == null)
        {
            triggerCollider = GetComponent<SphereCollider>();
        }

        if (_beamRenderer == null && lightBeamRoot != null)
        {
            _beamRenderer = lightBeamRoot.GetComponent<MeshRenderer>();
        }
    }

    private void ApplyVisuals()
    {
        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
            triggerCollider.radius = deliveryRadius;
            triggerCollider.center = new Vector3(0f, visualHeight * 0.5f, 0f);
        }

        if (visualRoot != null)
        {
            visualRoot.localPosition = new Vector3(0f, visualHeight * 0.5f, 0f);
            visualRoot.localRotation = Quaternion.identity;
            visualRoot.localScale = new Vector3(deliveryRadius * 2f, visualHeight * 0.5f, deliveryRadius * 2f);
        }

        if (lightBeamRoot != null)
        {
            lightBeamRoot.gameObject.SetActive(showLightBeam);
            lightBeamRoot.localPosition = new Vector3(0f, visualHeight, 0f);
            lightBeamRoot.localRotation = Quaternion.identity;
            lightBeamRoot.localScale = new Vector3(1.75f, visualHeight, 1.75f);
        }

        ApplyMaterial(visualRenderer, zoneColor);
        ApplyMaterial(_beamRenderer, beamColor);
    }

    private static void ApplyMaterial(Renderer renderer, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        Material material = renderer.sharedMaterial;
        if (material == null || (shader != null && material.shader != shader))
        {
            material = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
            renderer.sharedMaterial = material;
        }

        material.color = color;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }
}
