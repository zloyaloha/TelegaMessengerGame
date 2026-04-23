using UnityEngine;

// ИСПРАВЛЕНИЕ: удалено центрирование по halfWidth/halfHeight.
// Теперь используются абсолютные мировые координаты (x + offset.x),
// что обеспечивает непрерывность шума на стыках между чанками.
//
// ИСПРАВЛЕНИЕ 2: нормализация заменена с per-chunk (InverseLerp по локальному min/max)
// на теоретически-стабильную (по максимально возможной амплитуде fBm).
// Это сохраняет региональную вариацию амплитуды между чанками.
// Добавлен макро-шум, который задаёт крупные регионы: горы vs равнины.
public static class NoiseGenerator
{
    public sealed class Sampler
    {
        private readonly WorldSettings _settings;
        private readonly int _octaves;
        private readonly float _safeNoiseScale;
        private readonly float _safeMacroNoiseScale;
        private readonly float _safeRiverNoiseScale;
        private readonly float _safeRiverWarpScale;
        private readonly float _safeLakeNoiseScale;
        private readonly float _persistence;
        private readonly float _lacunarity;
        private readonly float _normalizeRange;
        private readonly Vector2[] _octaveOffsets;
        private readonly Vector2 _macroOffset;
        private readonly Vector2 _riverOffset;
        private readonly Vector2 _riverWarpOffset;
        private readonly Vector2 _lakeOffset;

        public Sampler(WorldSettings settings)
        {
            _settings = settings;
            if (_settings == null)
            {
                _octaves = 0;
                _safeNoiseScale = 1f;
                _safeMacroNoiseScale = 1f;
                _safeRiverNoiseScale = 1f;
                _safeRiverWarpScale = 1f;
                _safeLakeNoiseScale = 1f;
                _persistence = 0.5f;
                _lacunarity = 2f;
                _normalizeRange = 1f;
                _octaveOffsets = System.Array.Empty<Vector2>();
                return;
            }

            _octaves = Mathf.Max(1, _settings.octaves);
            _safeNoiseScale = Mathf.Max(0.01f, Mathf.Abs(_settings.noiseScale));
            _safeMacroNoiseScale = Mathf.Max(0.01f, Mathf.Abs(_settings.macroNoiseScale));
            _safeRiverNoiseScale = Mathf.Max(0.01f, Mathf.Abs(_settings.riverNoiseScale));
            _safeRiverWarpScale = Mathf.Max(0.01f, Mathf.Abs(_settings.riverWarpScale));
            _safeLakeNoiseScale = Mathf.Max(0.01f, Mathf.Abs(_settings.lakeNoiseScale));
            _persistence = Mathf.Clamp01(_settings.persistence);
            _lacunarity = Mathf.Max(1f, _settings.lacunarity);

            System.Random rng = new System.Random(_settings.seed);
            _octaveOffsets = new Vector2[_octaves];
            for (int i = 0; i < _octaves; i++)
            {
                _octaveOffsets[i] = new Vector2(
                    rng.Next(-100000, 100000),
                    rng.Next(-100000, 100000));
            }

            _macroOffset = new Vector2(
                rng.Next(-100000, 100000),
                rng.Next(-100000, 100000));
            _riverOffset = new Vector2(
                rng.Next(-100000, 100000),
                rng.Next(-100000, 100000));
            _riverWarpOffset = new Vector2(
                rng.Next(-100000, 100000),
                rng.Next(-100000, 100000));
            _lakeOffset = new Vector2(
                rng.Next(-100000, 100000),
                rng.Next(-100000, 100000));

            float maxAmplitude = 0f;
            float amplitude = 1f;
            for (int i = 0; i < _octaves; i++)
            {
                maxAmplitude += amplitude;
                amplitude *= _persistence;
            }

            _normalizeRange = Mathf.Max(0.0001f, maxAmplitude * 0.75f);
        }

        public float Sample01(float worldX, float worldY)
        {
            if (_settings == null)
                return 0f;

            float amplitude = 1f;
            float frequency = 1f;
            float noiseValue = 0f;
            float macro = 1f;

            for (int oct = 0; oct < _octaves; oct++)
            {
                float sx = worldX / _safeNoiseScale * frequency + _octaveOffsets[oct].x;
                float sy = worldY / _safeNoiseScale * frequency + _octaveOffsets[oct].y;

                noiseValue += (Mathf.PerlinNoise(sx, sy) * 2f - 1f) * amplitude;
                amplitude *= _persistence;
                frequency *= _lacunarity;
            }

            if (_settings.macroNoiseStrength > 0f)
            {
                float mx = worldX / _safeMacroNoiseScale + _macroOffset.x;
                float my = worldY / _safeMacroNoiseScale + _macroOffset.y;
                macro = Mathf.PerlinNoise(mx, my);
                float macroFactor = Mathf.Lerp(1f - _settings.macroNoiseStrength, 1f, macro);
                noiseValue *= macroFactor;
            }

            float normalized = Mathf.Clamp01((noiseValue + _normalizeRange) / (2f * _normalizeRange));
            return ApplyHydrology(
                _settings,
                worldX,
                worldY,
                macro,
                normalized,
                _riverOffset,
                _riverWarpOffset,
                _lakeOffset,
                _safeRiverNoiseScale,
                _safeRiverWarpScale,
                _safeLakeNoiseScale);
        }
    }

    public static float[,] Generate(WorldSettings settings, Vector2 offset = default, int width = 0, int height = 0)
    {
        if (settings == null)
            return new float[0, 0];

        if (width  <= 0) width  = settings.chunkWidth;
        if (height <= 0) height = settings.chunkHeight;

        float[,] map = new float[width, height];
        Sampler sampler = new Sampler(settings);

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float worldX = x + offset.x;
            float worldY = y + offset.y;
            map[x, y] = sampler.Sample01(worldX, worldY);
        }

        return map;
    }

    private static float ApplyHydrology(
        WorldSettings settings,
        float worldX,
        float worldY,
        float macro,
        float normalizedHeight,
        Vector2 riverOffset,
        Vector2 riverWarpOffset,
        Vector2 lakeOffset,
        float safeRiverNoiseScale,
        float safeRiverWarpScale,
        float safeLakeNoiseScale)
    {
        float carvedHeight = normalizedHeight;

        if (settings.riverStrength > 0f && safeRiverNoiseScale > 0.01f)
        {
            float warpSampleX = Mathf.PerlinNoise(
                worldX / safeRiverWarpScale + riverWarpOffset.x,
                worldY / safeRiverWarpScale + riverWarpOffset.y);
            float warpSampleY = Mathf.PerlinNoise(
                worldX / safeRiverWarpScale + riverWarpOffset.x + 37.31f,
                worldY / safeRiverWarpScale + riverWarpOffset.y + 19.73f);

            float warpedX = worldX + (warpSampleX - 0.5f) * settings.riverWarpStrength * safeRiverNoiseScale;
            float warpedY = worldY + (warpSampleY - 0.5f) * settings.riverWarpStrength * safeRiverNoiseScale;

            float riverNoise = Mathf.PerlinNoise(
                warpedX / safeRiverNoiseScale + riverOffset.x,
                warpedY / safeRiverNoiseScale + riverOffset.y);
            float riverCenter = 1f - Mathf.Abs(riverNoise * 2f - 1f);
            float riverMask = Mathf.SmoothStep(settings.riverThreshold, 1f, riverCenter);
            float valleyBias = Mathf.Lerp(1.15f, 0.55f, macro);
            carvedHeight -= riverMask * settings.riverDepth * settings.riverStrength * valleyBias;
        }

        if (settings.lakeStrength > 0f && safeLakeNoiseScale > 0.01f)
        {
            float lakeNoise = Mathf.PerlinNoise(
                worldX / safeLakeNoiseScale + lakeOffset.x,
                worldY / safeLakeNoiseScale + lakeOffset.y);
            float lakeMask = Mathf.SmoothStep(settings.lakeThreshold, 1f, lakeNoise);
            float lowlandBias = Mathf.Clamp01(1f - macro * 1.1f);
            carvedHeight -= lakeMask * settings.lakeDepth * settings.lakeStrength * lowlandBias;
        }

        if (float.IsNaN(carvedHeight) || float.IsInfinity(carvedHeight))
            carvedHeight = 0f;

        return Mathf.Clamp01(carvedHeight);
    }
}
