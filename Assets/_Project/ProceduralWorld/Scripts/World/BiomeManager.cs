using UnityEngine;

/// <summary>
/// Определяет биом для любой точки мира по высоте и температурному шуму.
/// Настраивается как ScriptableObject-ассет; подключается к WorldGenerator.
/// </summary>
[CreateAssetMenu(fileName = "BiomeManager", menuName = "ProceduralWorld/Biome Manager")]
public class BiomeManager : ScriptableObject
{
    [Header("Биомы (выбирается ближайший по высоте и температуре)")]
    [SerializeField] private BiomeData[] biomes;

    [Header("Шум температуры")]
    [SerializeField] private int   temperatureSeed  = 777;
    [Min(0.01f)]
    [SerializeField] private float temperatureScale = 200f;
    [SerializeField] private Vector2 temperatureOffset = Vector2.zero;
    [Range(0f, 2f)]
    [SerializeField] private float temperatureIntensity = 1f;
    [Range(1, 8)]
    [SerializeField] private int temperatureOctaves = 4;
    [Range(0f, 1f)]
    [SerializeField] private float temperaturePersistence = 0.5f;
    [Range(1f, 4f)]
    [SerializeField] private float temperatureLacunarity = 2f;

    // Кэшированное смещение для температурного шума
    private Vector2 _tempOffset;
    private bool    _ready;

    private void OnEnable()
    {
        _ready = false;
        EnsureReady();
    }

    /// <summary>
    /// Возвращает биом для мировой точки (worldX, worldZ) при абсолютной высоте в метрах.
    /// </summary>
    public BiomeData GetBiome(float worldX, float worldZ, float worldHeight, WorldSettings settings = null)
    {
        return GetBiome(CreateSample(worldX, worldZ, worldHeight), settings);
    }

    public BiomeData GetBiome(float worldX, float worldZ, float worldHeight, float slope, WorldSettings settings = null)
    {
        return GetBiome(CreateSample(worldX, worldZ, worldHeight, slope), settings);
    }

    public BiomeData GetBiome(BiomeSampleContext sample, WorldSettings settings = null)
    {
        EnsureReady();

        if (biomes == null || biomes.Length == 0) return null;

        BiomeData bestMatch = null;
        float bestMatchScore = float.MaxValue;
        BiomeData bestFallback = null;
        float bestFallbackScore = float.MaxValue;

        foreach (var b in biomes)
        {
            if (b == null) continue;

            BiomeConditionNode resolvedCondition = b.GetResolvedCondition(settings);
            bool matches = resolvedCondition == null || resolvedCondition.Evaluate(sample);
            float score = resolvedCondition?.Score(sample) ?? 0f;

            if (matches)
            {
                if (IsBetterCandidate(b, score, bestMatch, bestMatchScore))
                {
                    bestMatchScore = score;
                    bestMatch = b;
                }
            }
            else if (IsBetterCandidate(b, score, bestFallback, bestFallbackScore))
            {
                bestFallbackScore = score;
                bestFallback = b;
            }
        }

        return bestMatch != null ? bestMatch : bestFallback;
    }

    public BiomeData[] GetAllBiomes() => biomes;

    public int GetBiomeIndex(BiomeData biome)
    {
        if (biomes == null || biomes.Length == 0 || biome == null)
            return 0;

        for (int i = 0; i < biomes.Length; i++)
        {
            if (biomes[i] == biome)
                return i;
        }

        return 0;
    }

    public BiomeSampleContext CreateSample(float worldX, float worldZ, float worldHeight, float slope = 0f) =>
        new BiomeSampleContext(worldHeight, SampleTemperature(worldX, worldZ), slope);

    public float SampleTemperature(float worldX, float worldZ)
    {
        EnsureReady();

        float safeScale = Mathf.Max(0.01f, Mathf.Abs(temperatureScale));
        int safeOctaves = Mathf.Max(1, temperatureOctaves);
        float persistence = Mathf.Clamp01(temperaturePersistence);
        float lacunarity = Mathf.Max(1f, temperatureLacunarity);

        float amplitude = 1f;
        float frequency = 1f;
        float value = 0f;
        float maxAmplitude = 0f;

        for (int octave = 0; octave < safeOctaves; octave++)
        {
            float x = worldX / safeScale * frequency + _tempOffset.x + temperatureOffset.x;
            float z = worldZ / safeScale * frequency + _tempOffset.y + temperatureOffset.y;
            value += Mathf.PerlinNoise(x, z) * amplitude;
            maxAmplitude += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        float normalized = maxAmplitude > 0f ? value / maxAmplitude : 0.5f;
        return Mathf.Clamp01(0.5f + (normalized - 0.5f) * temperatureIntensity);
    }

    private static bool IsBetterCandidate(BiomeData candidate, float candidateScore, BiomeData current, float currentScore)
    {
        if (candidate == null) return false;
        if (current == null) return true;

        const float epsilon = 0.0001f;
        if (candidateScore < currentScore - epsilon)
            return true;

        if (Mathf.Abs(candidateScore - currentScore) <= epsilon &&
            candidate.selectionPriority > current.selectionPriority)
            return true;

        return false;
    }

    private void EnsureReady()
    {
        if (_ready) return;

        var rng = new System.Random(temperatureSeed);
        _tempOffset = new Vector2(rng.Next(-100000, 100000), rng.Next(-100000, 100000));
        _ready = true;
    }

    private void SanitizeNoiseSettings()
    {
        temperatureScale = Mathf.Max(0.01f, Mathf.Abs(temperatureScale));
        temperatureIntensity = Mathf.Clamp(temperatureIntensity, 0f, 2f);
        temperatureOctaves = Mathf.Max(1, temperatureOctaves);
        temperaturePersistence = Mathf.Clamp01(temperaturePersistence);
        temperatureLacunarity = Mathf.Max(1f, temperatureLacunarity);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        SanitizeNoiseSettings();
        _ready = false; // сбрасываем кэш температурного шума
        WorldGeneratorRefreshScheduler.RequestAll(WorldGeneratorRefreshMode.BiomesOnly);
    }
#endif
}
