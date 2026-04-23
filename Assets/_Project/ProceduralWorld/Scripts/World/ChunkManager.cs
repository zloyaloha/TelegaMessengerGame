using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Управляет динамической загрузкой и выгрузкой чанков вокруг игрока.
/// Работает только в Play Mode. Инициализируется через WorldGenerator.
/// </summary>
public class ChunkManager : MonoBehaviour
{
    [Header("Параметры стриминга")]
    [SerializeField] private int       viewDistanceInChunks = 3;
    [SerializeField] private Transform playerTransform;

    // Зависимости устанавливаются через Initialize()
    private WorldSettings    _settings;
    private ITerrainProvider _provider;
    private BiomeManager     _biomeManager;
    private Material         _terrainMaterial;

    private readonly Dictionary<Vector2Int, ChunkEntry> _active = new();
    private Vector2Int _lastPlayerChunk = new Vector2Int(int.MinValue, int.MinValue);

    public void Initialize(
        WorldSettings    settings,
        ITerrainProvider provider,
        BiomeManager     biomeManager,
        Material         terrainMaterial)
    {
        _settings        = settings;
        _provider        = provider;
        _biomeManager    = biomeManager;
        _terrainMaterial = terrainMaterial;
    }

    // Позволяет установить игрока из WorldGenerator или любого другого места
    public void SetPlayer(Transform player) => playerTransform = player;

    private void Update()
    {
        if (_settings == null || playerTransform == null) return;

        Vector2Int cur = WorldToChunkCoord(playerTransform.position);
        if (cur == _lastPlayerChunk) return;

        _lastPlayerChunk = cur;
        RefreshChunks(cur);
    }

    private void RefreshChunks(Vector2Int center)
    {
        var desired = new HashSet<Vector2Int>();
        for (int dy = -viewDistanceInChunks; dy <= viewDistanceInChunks; dy++)
        for (int dx = -viewDistanceInChunks; dx <= viewDistanceInChunks; dx++)
            desired.Add(new Vector2Int(center.x + dx, center.y + dy));

        var toRemove = new List<Vector2Int>();
        foreach (var coord in _active.Keys)
            if (!desired.Contains(coord)) toRemove.Add(coord);

        foreach (var coord in toRemove)
            RemoveChunk(coord);

        foreach (var coord in desired)
            if (!_active.ContainsKey(coord)) SpawnChunk(coord);
    }

    private void SpawnChunk(Vector2Int coord)
    {
        Mesh mesh = ChunkGenerator.BuildChunk(coord, _settings, _provider, _biomeManager);

        string chunkName = string.Concat("Chunk_", coord.x.ToString(), "_", coord.y.ToString());
        var go = new GameObject(chunkName);
        go.transform.SetParent(transform);
        go.transform.position = ChunkOrigin(coord);  // учитывает meshScale
        go.AddComponent<MeshFilter>().sharedMesh       = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = _terrainMaterial;
        var terrainCollider = go.AddComponent<MeshCollider>();
        terrainCollider.sharedMesh = mesh;

        // Размещаем объекты биома ПОСЛЕ появления MeshCollider
        if (_biomeManager != null)
        {
            int seed        = coord.x * 73856093 ^ coord.y * 19349663;
            float worldW = (_settings.chunkWidth  - 1) * _settings.meshScale;
            float worldH = (_settings.chunkHeight - 1) * _settings.meshScale;
            PropPlacer.PlaceProps(
                _biomeManager,
                _settings,
                go.transform.position,
                worldW,
                worldH,
                terrainCollider,
                go.transform,
                seed);
        }

        _active[coord] = new ChunkEntry { Go = go };
    }

    private void RemoveChunk(Vector2Int coord)
    {
        if (!_active.TryGetValue(coord, out var entry)) return;
        if (entry.Go != null) Destroy(entry.Go);
        _active.Remove(coord);
    }

    public void UnloadAll()
    {
        var keys = new List<Vector2Int>(_active.Keys);
        foreach (var coord in keys)
            RemoveChunk(coord);
    }

    private Vector2Int WorldToChunkCoord(Vector3 pos) => new Vector2Int(
        Mathf.FloorToInt(pos.x / ((_settings.chunkWidth  - 1) * _settings.meshScale)),
        Mathf.FloorToInt(pos.z / ((_settings.chunkHeight - 1) * _settings.meshScale)));

    private Vector3 ChunkOrigin(Vector2Int coord) => new Vector3(
        coord.x * (_settings.chunkWidth  - 1) * _settings.meshScale,
        0f,
        coord.y * (_settings.chunkHeight - 1) * _settings.meshScale);

    private struct ChunkEntry
    {
        public GameObject Go;
    }
}
