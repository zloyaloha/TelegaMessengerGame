using UnityEngine;

[CreateAssetMenu(fileName = "WorldSettings", menuName = "ProceduralWorld/World Settings")]
public class WorldSettings : ScriptableObject
{
    [Header("Шум Перлина")]

    [Header("Размер мира")]
    public int   chunkWidth        = 64;
    public int   chunkHeight       = 64;
    public int   worldSizeInChunks = 4;
    // Сколько мировых единиц занимает одно ребро между вершинами по X/Z.
    // Увеличивай это, а не Transform.scale — иначе сломается высотный шейдер.
    [Min(0.1f)]
    public float meshScale         = 3f;

    public float noiseScale = 50f;

    [Range(1, 8)]
    public int octaves = 4;

    [Range(0f, 1f)]
    public float persistence = 0.5f;

    [Range(1f, 4f)]
    public float lacunarity = 2f;

    public int seed = 42;

    [Header("Макро-шум (региональное разнообразие рельефа)")]
    // Масштаб макро-шума: больше → крупнее регионы (горы / равнины)
    public float macroNoiseScale = 300f;
    // Сила влияния: 0 = всё одинаково, 1 = от полностью равнины до полных гор
    [Range(0f, 1f)]
    public float macroNoiseStrength = 0.7f;

    [Header("Гидрология")]
    [Range(0f, 1f)] public float riverStrength = 0f;
    public float riverNoiseScale = 220f;
    public float riverWarpScale = 140f;
    [Range(0f, 2f)] public float riverWarpStrength = 0.75f;
    [Range(0f, 1f)] public float riverThreshold = 0.72f;
    [Range(0f, 1f)] public float riverDepth = 0.16f;
    [Range(0f, 1f)] public float lakeStrength = 0f;
    public float lakeNoiseScale = 180f;
    [Range(0f, 1f)] public float lakeThreshold = 0.78f;
    [Range(0f, 1f)] public float lakeDepth = 0.22f;

    [Header("Высота рельефа")]
    public float heightMultiplier = 20f;

    public AnimationCurve heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Вода")]
    [Range(-100f, 300f)]
    public float waterLevel = 5f;

    public float EvaluateHeight(float noiseValue)
    {
        if (float.IsNaN(noiseValue) || float.IsInfinity(noiseValue))
            noiseValue = 0f;

        float safeNoise = Mathf.Clamp01(noiseValue);
        AnimationCurve curve = heightCurve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
        float evaluated = curve.Evaluate(safeNoise) * heightMultiplier;
        return float.IsFinite(evaluated) ? evaluated : 0f;
    }

    public Vector2 EvaluateHeightRange(float noiseMin, float noiseMax, int samples = 32)
    {
        float safeMin = Mathf.Clamp01(Mathf.Min(noiseMin, noiseMax));
        float safeMax = Mathf.Clamp01(Mathf.Max(noiseMin, noiseMax));

        if (Mathf.Approximately(safeMin, safeMax))
        {
            float height = EvaluateHeight(safeMin);
            return new Vector2(height, height);
        }

        int sampleCount = Mathf.Max(2, samples);
        float minHeight = float.PositiveInfinity;
        float maxHeight = float.NegativeInfinity;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (sampleCount - 1f);
            float noiseValue = Mathf.Lerp(safeMin, safeMax, t);
            float height = EvaluateHeight(noiseValue);
            minHeight = Mathf.Min(minHeight, height);
            maxHeight = Mathf.Max(maxHeight, height);
        }

        return new Vector2(minHeight, maxHeight);
    }

    #if UNITY_EDITOR
    private void OnValidate()
    {
        chunkWidth = Mathf.Max(2, chunkWidth);
        chunkHeight = Mathf.Max(2, chunkHeight);
        worldSizeInChunks = Mathf.Max(1, worldSizeInChunks);
        meshScale = Mathf.Max(0.1f, meshScale);
        noiseScale = Mathf.Max(0.01f, Mathf.Abs(noiseScale));
        octaves = Mathf.Max(1, octaves);
        persistence = Mathf.Clamp01(persistence);
        lacunarity = Mathf.Max(1f, lacunarity);
        macroNoiseScale = Mathf.Max(0.01f, Mathf.Abs(macroNoiseScale));
        macroNoiseStrength = Mathf.Clamp01(macroNoiseStrength);
        riverNoiseScale = Mathf.Max(0.01f, Mathf.Abs(riverNoiseScale));
        riverWarpScale = Mathf.Max(0.01f, Mathf.Abs(riverWarpScale));
        riverWarpStrength = Mathf.Clamp(riverWarpStrength, 0f, 2f);
        riverThreshold = Mathf.Clamp01(riverThreshold);
        riverDepth = Mathf.Clamp01(riverDepth);
        lakeNoiseScale = Mathf.Max(0.01f, Mathf.Abs(lakeNoiseScale));
        lakeThreshold = Mathf.Clamp01(lakeThreshold);
        lakeDepth = Mathf.Clamp01(lakeDepth);
        heightMultiplier = Mathf.Max(0f, heightMultiplier);
        heightCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);

        var previews = FindObjectsByType<NoisePreview>(FindObjectsSortMode.None);
        foreach (var preview in previews)
            preview.UpdatePreview();

        WorldGeneratorRefreshScheduler.RequestAll(WorldGeneratorRefreshMode.Full);
    }
    #endif
}
