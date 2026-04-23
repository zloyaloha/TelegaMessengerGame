using System;
using UnityEngine;
using System.Collections.Generic;

public enum WorldGeneratorRefreshMode
{
    Full,
    BiomesOnly,
}

/// <summary>
/// Точка входа в систему генерации мира.
/// В Edit Mode генерирует статический мир (кнопка в инспекторе).
/// В Play Mode с флагом streamingMode передаёт управление ChunkManager-у.
/// </summary>
[ExecuteAlways]
public class WorldGenerator : MonoBehaviour
{
    private const int MaxTerrainBiomeTextureSlots = 8;
    private static readonly int[] TerrainBiomeTextureIds = new int[MaxTerrainBiomeTextureSlots];
    private static readonly int[] TerrainBiomeScaleIds = new int[MaxTerrainBiomeTextureSlots];
    private static readonly int TerrainBiomeLayerCountId = Shader.PropertyToID("_BiomeLayerCount");

    static WorldGenerator()
    {
        for (int i = 0; i < MaxTerrainBiomeTextureSlots; i++)
        {
            TerrainBiomeTextureIds[i] = Shader.PropertyToID($"_BiomeTex{i}");
            TerrainBiomeScaleIds[i] = Shader.PropertyToID($"_BiomeScale{i}");
        }
    }

    [Header("Настройки мира")]
    [SerializeField] private WorldSettings settings;
    [SerializeField] private Material terrainMaterial;

    [Header("Поставщики")]
    [SerializeField] private PerlinTerrainProvider terrainProvider;
    [SerializeField] private BiomeManager biomeManager;

    [Header("Вода")]
    [SerializeField] private Material waterMaterial; // авто-создаётся если пусто

    [Header("Стриминг (только Play Mode)")]
    [SerializeField] private bool         streamingMode = false;
    [SerializeField] private ChunkManager chunkManager;

    private Dictionary<Vector2Int, GameObject> _chunks = new();
    private bool _isRefreshing;
    private NoiseGenerator.Sampler _terrainNoiseSampler;

    public event Action OnWorldGenerated;

    public WorldSettings Settings => settings;
    public BiomeManager BiomeManager => biomeManager;
    public float WorldWidth => settings != null
        ? settings.worldSizeInChunks * (settings.chunkWidth - 1) * settings.meshScale
        : 0f;
    public float WorldDepth => settings != null
        ? settings.worldSizeInChunks * (settings.chunkHeight - 1) * settings.meshScale
        : 0f;
    public Rect WorldBoundsXZ => new Rect(transform.position.x, transform.position.z, WorldWidth, WorldDepth);

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        InvalidateTerrainSampler();
        ConfigureTerrainMaterial();

        if (!Application.isPlaying || !streamingMode) return;

        if (chunkManager != null)
            chunkManager.Initialize(settings, terrainProvider, biomeManager, terrainMaterial);
    }

    private void Start()
    {
        if (Application.isPlaying && !streamingMode)
            GenerateWorld();
    }

    private void OnValidate()
    {
        InvalidateTerrainSampler();
#if UNITY_EDITOR
        WorldGeneratorRefreshScheduler.Request(this, WorldGeneratorRefreshMode.Full);
#endif
    }

    // ── Публичные методы (вызываются из кастомного редактора) ─────────────────

    public void GenerateWorld()
    {
        Refresh(WorldGeneratorRefreshMode.Full);
    }

    public void RefreshBiomes()
    {
        Refresh(WorldGeneratorRefreshMode.BiomesOnly);
    }

    public void Refresh(WorldGeneratorRefreshMode mode)
    {
        if (_isRefreshing) return;
        if (settings == null || terrainMaterial == null) return;

        ConfigureTerrainMaterial();
        InvalidateTerrainSampler();

        _isRefreshing = true;
        bool refreshCompleted = false;
        try
        {
            if (mode == WorldGeneratorRefreshMode.Full)
                RebuildTerrain();
            else
                RebuildBiomesOnly();

            refreshCompleted = true;
        }
        finally
        {
            _isRefreshing = false;
            if (refreshCompleted)
                OnWorldGenerated?.Invoke();
        }
    }

    public NoiseGenerator.Sampler CreateTerrainNoiseSampler()
    {
        if (_terrainNoiseSampler == null && settings != null)
            _terrainNoiseSampler = new NoiseGenerator.Sampler(settings);

        return _terrainNoiseSampler;
    }

    public Vector2 GetTerrainHeightRange() =>
        settings != null ? settings.EvaluateHeightRange(0f, 1f) : Vector2.zero;

    public Vector2 WorldToNormalizedPosition(Vector3 worldPosition) =>
        WorldToNormalizedPosition(worldPosition.x, worldPosition.z);

    public Vector2 WorldToNormalizedPosition(float worldX, float worldZ)
    {
        Rect bounds = WorldBoundsXZ;
        if (bounds.width <= Mathf.Epsilon || bounds.height <= Mathf.Epsilon)
            return new Vector2(0.5f, 0.5f);

        return new Vector2(
            (worldX - bounds.xMin) / bounds.width,
            (worldZ - bounds.yMin) / bounds.height);
    }

    public float SampleTerrainHeight(float worldX, float worldZ) =>
        SampleTerrainHeight(worldX, worldZ, CreateTerrainNoiseSampler());

    public float SampleTerrainHeight(float worldX, float worldZ, NoiseGenerator.Sampler sampler)
    {
        if (settings == null)
            return 0f;

        NoiseGenerator.Sampler activeSampler = sampler ?? CreateTerrainNoiseSampler();
        if (activeSampler == null)
            return 0f;

        Vector2 sampleCoords = WorldToTerrainNoiseCoordinates(worldX, worldZ);
        float normalizedHeight = activeSampler.Sample01(sampleCoords.x, sampleCoords.y);
        return settings.EvaluateHeight(normalizedHeight);
    }

    public float SampleTerrainSlope(float worldX, float worldZ, float sampleStep = -1f) =>
        SampleTerrainSlope(worldX, worldZ, CreateTerrainNoiseSampler(), sampleStep);

    public float SampleTerrainSlope(float worldX, float worldZ, NoiseGenerator.Sampler sampler, float sampleStep = -1f)
    {
        if (settings == null)
            return 0f;

        float step = sampleStep > 0f ? sampleStep : Mathf.Max(0.1f, settings.meshScale);
        float left = SampleTerrainHeight(worldX - step, worldZ, sampler);
        float right = SampleTerrainHeight(worldX + step, worldZ, sampler);
        float bottom = SampleTerrainHeight(worldX, worldZ - step, sampler);
        float top = SampleTerrainHeight(worldX, worldZ + step, sampler);

        float dx = (right - left) / (2f * step);
        float dz = (top - bottom) / (2f * step);
        Vector3 normal = new Vector3(-dx, 1f, -dz).normalized;
        return Vector3.Angle(normal, Vector3.up);
    }

    private void RebuildTerrain()
    {
        RebuildDictionaryFromChildren();

        var desiredCoords = new HashSet<Vector2Int>();
        for (int cy = 0; cy < settings.worldSizeInChunks; cy++)
        for (int cx = 0; cx < settings.worldSizeInChunks; cx++)
            desiredCoords.Add(new Vector2Int(cx, cy));

        // Удаляем чанки, которых больше нет в desiredCoords
        var toRemove = new List<Vector2Int>();
        foreach (var coord in _chunks.Keys)
            if (!desiredCoords.Contains(coord)) toRemove.Add(coord);
        foreach (var coord in toRemove)
        {
            DestroyImmediate(_chunks[coord]);
            _chunks.Remove(coord);
        }

        // Создаём или обновляем нужные чанки
        foreach (var coord in desiredCoords)
        {
            Mesh mesh = ChunkGenerator.BuildChunk(coord, settings, terrainProvider, biomeManager);

            if (_chunks.TryGetValue(coord, out var go) && go != null)
            {
                go.GetComponent<MeshFilter>().sharedMesh   = mesh;
                go.GetComponent<MeshCollider>().sharedMesh = mesh;
                // Позицию тоже обновляем — она меняется при изменении meshScale
                go.transform.position = new Vector3(
                    coord.x * (settings.chunkWidth  - 1) * settings.meshScale,
                    0f,
                    coord.y * (settings.chunkHeight - 1) * settings.meshScale);
            }
            else
            {
                SpawnChunk(coord, mesh);
            }
        }

        StitchBoundaries();
        RefreshChunkColliders();
        SpawnWaterPlane();

        // Пропсы биомов — только если поставщики подключены
        if (biomeManager != null && terrainProvider != null)
        {
            Physics.SyncTransforms(); // Синхронизируем MeshCollider-ы перед Raycast
            PlaceAllProps();
        }
    }

    private void RebuildBiomesOnly()
    {
        RebuildDictionaryFromChildren();
        if (_chunks.Count == 0)
        {
            RebuildTerrain();
            return;
        }

        foreach (var kvp in _chunks)
            RefreshChunkBiomeData(kvp.Value);

        if (biomeManager != null && terrainProvider != null)
        {
            Physics.SyncTransforms();
            PlaceAllProps();
        }
    }

    public void ClearWorld()
    {
        RebuildDictionaryFromChildren();
        foreach (var chunk in _chunks.Values)
            if (chunk != null) DestroyImmediate(chunk);
        _chunks.Clear();

        var water = transform.Find("WaterPlane");
        if (water != null) DestroyImmediate(water.gameObject);
    }

    // ── Приватные методы ──────────────────────────────────────────────────────

    // ── Вода ─────────────────────────────────────────────────────────────────

    private void SpawnWaterPlane()
    {
        // Удаляем старую плоскость если есть
        var existing = transform.Find("WaterPlane");
        if (existing != null) DestroyImmediate(existing.gameObject);

        EnsureWaterMaterial();
        if (waterMaterial == null) return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "WaterPlane";
        go.transform.SetParent(transform);

        float worldX = settings.worldSizeInChunks * (settings.chunkWidth  - 1) * settings.meshScale;
        float worldZ = settings.worldSizeInChunks * (settings.chunkHeight - 1) * settings.meshScale;

        // Центрируем плоскость по всему миру и ставим на уровень воды
        go.transform.position   = new Vector3(worldX * 0.5f, settings.waterLevel, worldZ * 0.5f);
        go.transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = new Vector3(worldX, worldZ, 1f);

        go.GetComponent<MeshRenderer>().sharedMaterial = waterMaterial;

        // Вода не должна участвовать в физике
        DestroyImmediate(go.GetComponent<MeshCollider>());
    }

    private void EnsureWaterMaterial()
    {
        if (waterMaterial != null) return;

        var shader = Shader.Find("ProceduralWorld/Water");
        if (shader == null)
        {
            Debug.LogWarning("WorldGenerator: шейдер ProceduralWorld/Water не найден. " +
                             "Убедись, что файл Water.shader есть в папке _Project/ProceduralWorld/Shaders/");
            return;
        }

#if UNITY_EDITOR
        // Создаём и сохраняем ассет материала, чтобы он не терялся
        const string folder  = "Assets/_Project/ProceduralWorld/Materials";
        const string matPath = folder + "/Water.mat";

        if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
            UnityEditor.AssetDatabase.CreateFolder("Assets/_Project/ProceduralWorld", "Materials");

        var mat = new Material(shader);
        ApplyWaterDefaults(mat);
        UnityEditor.AssetDatabase.CreateAsset(mat, matPath);
        UnityEditor.AssetDatabase.SaveAssets();
        waterMaterial = mat;
#else
        waterMaterial = new Material(shader);
        ApplyWaterDefaults(waterMaterial);
#endif
    }

    private static void ApplyWaterDefaults(Material mat)
    {
        mat.SetColor("_ShallowColor", new Color(0.28f, 0.76f, 0.93f, 0.50f));
        mat.SetColor("_DeepColor",    new Color(0.04f, 0.20f, 0.52f, 0.90f));
        mat.SetFloat("_DepthFade",    8f);
        mat.SetFloat("_FresnelPower", 3f);
    }

    private void SpawnChunk(Vector2Int coord, Mesh mesh)
    {
        var chunk = new GameObject($"Chunk_{coord.x}_{coord.y}");
        chunk.transform.SetParent(transform);
        chunk.transform.position = new Vector3(
            coord.x * (settings.chunkWidth  - 1) * settings.meshScale,
            0f,
            coord.y * (settings.chunkHeight - 1) * settings.meshScale);
        chunk.AddComponent<MeshFilter>().sharedMesh       = mesh;
        chunk.AddComponent<MeshRenderer>().sharedMaterial = terrainMaterial;
        chunk.AddComponent<MeshCollider>().sharedMesh     = mesh;
        _chunks[coord] = chunk;
    }

    // Сшивает высоты на стыках (копирует от левого/нижнего чанка)
    // и усредняет нормали — без RecalculateNormals, чтобы сохранить
    // нормали, рассчитанные с учётом бордерных вершин.
    private void StitchBoundaries()
    {
        int w = settings.chunkWidth;
        int h = settings.chunkHeight;
        int size = settings.worldSizeInChunks;

        // Горизонтальные стыки: правый край mL (w-1, y) → левый край mR (0, y).
        for (int cy = 0; cy < size; cy++)
        for (int cx = 0; cx < size - 1; cx++)
            if (TryGetMesh(cx, cy, out var mL) && TryGetMesh(cx + 1, cy, out var mR))
                StitchEdge(mL, mR, h, srcOffset: w - 1, dstOffset: 0, step: w);

        // Вертикальные стыки: верхний край mB (x, h-1) → нижний край mT (x, 0).
        for (int cy = 0; cy < size - 1; cy++)
        for (int cx = 0; cx < size; cx++)
            if (TryGetMesh(cx, cy, out var mB) && TryGetMesh(cx, cy + 1, out var mT))
                StitchEdge(mB, mT, w, srcOffset: (h - 1) * w, dstOffset: 0, step: 1);
    }

    // Копирует высоту из src в dst и усредняет нормали на общей кромке за один проход.
    private static void StitchEdge(Mesh src, Mesh dst, int count, int srcOffset, int dstOffset, int step)
    {
        var vSrc = src.vertices; var vDst = dst.vertices;
        var nSrc = src.normals;  var nDst = dst.normals;
        for (int i = 0; i < count; i++)
        {
            int iSrc = srcOffset + i * step;
            int iDst = dstOffset + i * step;
            vDst[iDst] = new Vector3(vDst[iDst].x, vSrc[iSrc].y, vDst[iDst].z);
            var avg = (nSrc[iSrc] + nDst[iDst]).normalized;
            nSrc[iSrc] = avg; nDst[iDst] = avg;
        }
        dst.vertices = vDst;
        src.normals = nSrc; dst.normals = nDst;
    }

    private bool TryGetMesh(int cx, int cy, out Mesh mesh)
    {
        mesh = null;
        if (!_chunks.TryGetValue(new Vector2Int(cx, cy), out var go) || go == null) return false;
        mesh = go.GetComponent<MeshFilter>().sharedMesh;
        return mesh != null;
    }

    private void ConfigureTerrainMaterial()
    {
        if (terrainMaterial == null)
            return;

        BiomeData[] biomes = biomeManager != null ? biomeManager.GetAllBiomes() : null;
        int layerCount = biomes != null ? Mathf.Min(biomes.Length, MaxTerrainBiomeTextureSlots) : 0;

        for (int i = 0; i < MaxTerrainBiomeTextureSlots; i++)
        {
            Texture texture = Texture2D.whiteTexture;
            float worldSize = 24f;

            if (biomes != null && i < biomes.Length && biomes[i] != null)
            {
                texture = biomes[i].groundTexture != null
                    ? biomes[i].groundTexture
                    : Texture2D.whiteTexture;
                worldSize = Mathf.Max(0.1f, biomes[i].groundTextureWorldSize);
            }

            terrainMaterial.SetTexture(TerrainBiomeTextureIds[i], texture);
            terrainMaterial.SetFloat(TerrainBiomeScaleIds[i], worldSize);
        }

        terrainMaterial.SetFloat(TerrainBiomeLayerCountId, Mathf.Max(1, layerCount));

#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.EditorUtility.SetDirty(terrainMaterial);
#endif
    }

    // ── Пропсы ───────────────────────────────────────────────────────────────

    private void PlaceAllProps()
    {
        foreach (var kvp in _chunks)
            PlaceChunkProps(kvp.Key, kvp.Value);
    }

    private void PlaceChunkProps(Vector2Int coord, GameObject chunkGo)
    {
        if (chunkGo == null) return;

        // Удаляем старый контейнер пропсов перед пересозданием
        var existing = chunkGo.transform.Find("Props");
        if (existing != null) DestroyImmediate(existing.gameObject);

        float worldW  = (settings.chunkWidth  - 1) * settings.meshScale;
        float worldH  = (settings.chunkHeight - 1) * settings.meshScale;

        var propsGo = new GameObject("Props");
        propsGo.transform.SetParent(chunkGo.transform);
        propsGo.transform.localPosition = Vector3.zero;

        int seed = coord.x * 73856093 ^ coord.y * 19349663;
        var terrainCollider = chunkGo.GetComponent<MeshCollider>();
        PropPlacer.PlaceProps(
            biomeManager,
            settings,
            chunkGo.transform.position,
            worldW,
            worldH,
            terrainCollider,
            propsGo.transform,
            seed);
    }

    private void RefreshChunkBiomeData(GameObject chunkGo)
    {
        if (chunkGo == null || biomeManager == null) return;

        var meshFilter = chunkGo.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null) return;

        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        if (vertices == null || vertices.Length == 0) return;

        var colors = new Color[vertices.Length];
        var biomeData = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldPos = chunkGo.transform.position + vertices[i];
            float slope = normals != null && i < normals.Length
                ? Vector3.Angle(normals[i], Vector3.up)
                : 0f;

            BiomeData biome = biomeManager.GetBiome(worldPos.x, worldPos.z, worldPos.y, slope, settings);
            colors[i] = biome != null ? biome.terrainTint : Color.white;
            biomeData[i] = new Vector2(
                biome != null ? biomeManager.GetBiomeIndex(biome) : 0f,
                Mathf.Clamp01(slope / 90f));
        }

        mesh.colors = colors;
        mesh.uv2 = biomeData;
        if (chunkGo.TryGetComponent<MeshRenderer>(out var meshRenderer))
            meshRenderer.sharedMaterial = terrainMaterial;
    }

    private void RefreshChunkColliders()
    {
        foreach (var chunk in _chunks.Values)
        {
            if (chunk == null) continue;
            if (!chunk.TryGetComponent<MeshCollider>(out var collider)) continue;
            if (!chunk.TryGetComponent<MeshFilter>(out var meshFilter)) continue;

            collider.sharedMesh = null;
            collider.sharedMesh = meshFilter.sharedMesh;
        }
    }

    private void RebuildDictionaryFromChildren()
    {
        _chunks.Clear();
        foreach (Transform child in transform)
        {
            var parts = child.name.Split('_');
            if (parts.Length == 3
                && int.TryParse(parts[1], out int x)
                && int.TryParse(parts[2], out int y))
                _chunks[new Vector2Int(x, y)] = child.gameObject;
        }
    }

    private Vector2 WorldToTerrainNoiseCoordinates(float worldX, float worldZ)
    {
        float safeMeshScale = settings != null ? Mathf.Max(0.0001f, settings.meshScale) : 1f;
        return new Vector2(
            (worldX - transform.position.x) / safeMeshScale,
            (worldZ - transform.position.z) / safeMeshScale);
    }

    private void InvalidateTerrainSampler()
    {
        _terrainNoiseSampler = null;
    }

#if UNITY_EDITOR
    public void ProcessEditorRefresh(WorldGeneratorRefreshMode mode)
    {
        if (this == null || Application.isPlaying || !isActiveAndEnabled)
            return;

        Refresh(mode);
    }
#endif
}
