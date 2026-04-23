using System;
using UnityEngine;

[DisallowMultipleComponent]
public class MinimapGenerator : MonoBehaviour
{
    [SerializeField] private WorldGenerator worldGenerator;
    [SerializeField] private BiomeManager biomeManager;
    [SerializeField, Min(16)] private int textureResolution = 256;
    [SerializeField] private bool shadeByHeight = true;
    [SerializeField, Range(0f, 0.5f)] private float heightShadeStrength = 0.18f;
    [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;

    private Texture2D _minimapTexture;

    public event Action<Texture2D> TextureGenerated;

    public Texture2D MinimapTexture => _minimapTexture;
    public Rect WorldBoundsXZ => worldGenerator != null ? worldGenerator.WorldBoundsXZ : default;
    public int TextureResolution => Mathf.Max(16, textureResolution);

    private void Awake()
    {
        ResolveDependencies();
    }

    private void OnEnable()
    {
        ResolveDependencies();
        Subscribe();

        if (Application.isPlaying && _minimapTexture == null && HasRequiredContext())
            GenerateMinimap();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        ReleaseTexture();
    }

    private void Reset()
    {
        worldGenerator = GetComponent<WorldGenerator>();
        if (worldGenerator == null)
            worldGenerator = FindFirstObjectByType<WorldGenerator>();

        biomeManager = worldGenerator != null ? worldGenerator.BiomeManager : FindFirstObjectByType<WorldGenerator>()?.BiomeManager;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        textureResolution = Mathf.Max(16, textureResolution);
        ResolveDependencies();
    }
#endif

    public void GenerateMinimap()
    {
        ResolveDependencies();
        if (!HasRequiredContext())
            return;

        Rect bounds = worldGenerator.WorldBoundsXZ;
        if (bounds.width <= Mathf.Epsilon || bounds.height <= Mathf.Epsilon)
            return;

        EnsureTexture();

        NoiseGenerator.Sampler sampler = worldGenerator.CreateTerrainNoiseSampler();
        Vector2 terrainHeightRange = worldGenerator.GetTerrainHeightRange();
        Color[] pixels = new Color[TextureResolution * TextureResolution];

        for (int py = 0; py < TextureResolution; py++)
        {
            for (int px = 0; px < TextureResolution; px++)
            {
                Vector3 worldPoint = PixelToWorld(px, py);
                float terrainHeight = worldGenerator.SampleTerrainHeight(worldPoint.x, worldPoint.z, sampler);
                float slope = worldGenerator.SampleTerrainSlope(worldPoint.x, worldPoint.z, sampler);
                BiomeData biome = biomeManager.GetBiome(worldPoint.x, worldPoint.z, terrainHeight, slope, worldGenerator.Settings);

                Color color = biome != null ? biome.minimapColor : Color.black;
                if (shadeByHeight)
                    color = ApplyHeightShading(color, terrainHeight, terrainHeightRange);

                pixels[py * TextureResolution + px] = color;
            }
        }

        _minimapTexture.SetPixels(pixels);
        _minimapTexture.Apply(false, false);
        TextureGenerated?.Invoke(_minimapTexture);
    }

    public Vector2 WorldToNormalizedCoordinates(Vector3 worldPosition) =>
        WorldToNormalizedCoordinates(worldPosition.x, worldPosition.z);

    public Vector2 WorldToNormalizedCoordinates(float worldX, float worldZ)
    {
        if (worldGenerator == null)
            return new Vector2(0.5f, 0.5f);

        return worldGenerator.WorldToNormalizedPosition(worldX, worldZ);
    }

    public Vector3 PixelToWorld(int pixelX, int pixelY)
    {
        Rect bounds = WorldBoundsXZ;
        float u = (pixelX + 0.5f) / TextureResolution;
        float v = (pixelY + 0.5f) / TextureResolution;

        return new Vector3(
            Mathf.Lerp(bounds.xMin, bounds.xMax, u),
            0f,
            Mathf.Lerp(bounds.yMin, bounds.yMax, v));
    }

    private Color ApplyHeightShading(Color baseColor, float height, Vector2 terrainHeightRange)
    {
        if (terrainHeightRange.y - terrainHeightRange.x <= Mathf.Epsilon)
            return baseColor;

        float normalizedHeight = Mathf.InverseLerp(terrainHeightRange.x, terrainHeightRange.y, height);
        float brightness = Mathf.Lerp(1f - heightShadeStrength, 1f + heightShadeStrength, normalizedHeight);
        return new Color(
            Mathf.Clamp01(baseColor.r * brightness),
            Mathf.Clamp01(baseColor.g * brightness),
            Mathf.Clamp01(baseColor.b * brightness),
            baseColor.a);
    }

    private void HandleWorldGenerated()
    {
        GenerateMinimap();
    }

    private void ResolveDependencies()
    {
        if (worldGenerator == null)
            worldGenerator = GetComponent<WorldGenerator>() ?? FindFirstObjectByType<WorldGenerator>();

        if (biomeManager == null && worldGenerator != null)
            biomeManager = worldGenerator.BiomeManager;
    }

    private bool HasRequiredContext() =>
        worldGenerator != null &&
        worldGenerator.Settings != null &&
        biomeManager != null;

    private void Subscribe()
    {
        if (worldGenerator != null)
            worldGenerator.OnWorldGenerated += HandleWorldGenerated;
    }

    private void Unsubscribe()
    {
        if (worldGenerator != null)
            worldGenerator.OnWorldGenerated -= HandleWorldGenerated;
    }

    private void EnsureTexture()
    {
        if (_minimapTexture != null &&
            _minimapTexture.width == TextureResolution &&
            _minimapTexture.height == TextureResolution)
        {
            _minimapTexture.filterMode = filterMode;
            _minimapTexture.wrapMode = TextureWrapMode.Clamp;
            return;
        }

        ReleaseTexture();

        _minimapTexture = new Texture2D(TextureResolution, TextureResolution, TextureFormat.RGBA32, false)
        {
            name = "WorldMinimap",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = filterMode
        };
    }

    private void ReleaseTexture()
    {
        if (_minimapTexture == null)
            return;

        if (Application.isPlaying)
            Destroy(_minimapTexture);
        else
            DestroyImmediate(_minimapTexture);

        _minimapTexture = null;
    }
}
