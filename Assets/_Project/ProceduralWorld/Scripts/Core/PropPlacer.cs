using UnityEngine;

/// <summary>
/// Статический класс для размещения объектов биома (деревья, камни и т.д.)
/// на поверхности чанка через Physics.Raycast по нормали поверхности.
/// Вызывается после того, как чанк добавлен на сцену (нужен MeshCollider).
/// </summary>
public static class PropPlacer
{
    private const float WaterSurfaceClearance = 0.05f;

    public static void PlaceProps(
        BiomeManager biomeManager,
        WorldSettings settings,
        Vector3 chunkWorldPos,
        float worldWidth,
        float worldHeight,
        Collider terrainCollider,
        Transform parent,
        int seed)
    {
        if (biomeManager == null || settings == null || terrainCollider == null) return;

        BiomeData[] biomes = biomeManager.GetAllBiomes();
        if (biomes == null || biomes.Length == 0) return;

        float maxDensity = 0f;
        foreach (var biome in biomes)
        {
            if (biome == null || biome.props == null || biome.props.Length == 0) continue;
            maxDensity = Mathf.Max(maxDensity, biome.propDensity);
        }

        if (maxDensity <= 0f) return;

        var rng = new System.Random(seed);
        int sampleCount = Mathf.RoundToInt(worldWidth * worldHeight * maxDensity);
        var totalWeights = new System.Collections.Generic.Dictionary<BiomeData, float>();
        float maxMinDistance = GetMaxMinDistance(biomes);
        var occupiedCells = maxMinDistance > 0f
            ? new System.Collections.Generic.Dictionary<Vector2Int, System.Collections.Generic.List<Vector3>>()
            : null;

        for (int i = 0; i < sampleCount; i++)
        {
            float px = (float)rng.NextDouble() * worldWidth;
            float pz = (float)rng.NextDouble() * worldHeight;

            Vector3 rayOrigin = chunkWorldPos + new Vector3(px, 500f, pz);
            if (!terrainCollider.Raycast(new Ray(rayOrigin, Vector3.down), out RaycastHit hit, 1000f)) continue;
            if (IsUnderwater(hit.point, settings)) continue;

            float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
            BiomeData localBiome = biomeManager.GetBiome(hit.point.x, hit.point.z, hit.point.y, slopeAngle, settings);
            if (localBiome == null || localBiome.props == null || localBiome.props.Length == 0) continue;

            float densityChance = Mathf.Clamp01(localBiome.propDensity / maxDensity);
            if ((float)rng.NextDouble() > densityChance) continue;

            float totalWeight = GetTotalWeight(localBiome, totalWeights);
            if (totalWeight <= 0f) continue;

            WeightedPrefab chosen = PickWeightedRandom(localBiome.props, totalWeight, rng);
            if (chosen?.prefab == null)            continue;
            if (chosen.spawnChance <= 0f || (float)rng.NextDouble() > chosen.spawnChance) continue;
            if (slopeAngle > chosen.maxSlopeAngle) continue;
            if (IsTooClose(hit.point, chosen.minDistance, maxMinDistance, occupiedCells)) continue;

            float yRot   = (float)rng.NextDouble() * 360f;
            float scaleX = Mathf.Lerp(chosen.scaleMin.x, chosen.scaleMax.x, (float)rng.NextDouble());
            float scaleY = Mathf.Lerp(chosen.scaleMin.y, chosen.scaleMax.y, (float)rng.NextDouble());
            float scaleZ = Mathf.Lerp(chosen.scaleMin.z, chosen.scaleMax.z, (float)rng.NextDouble());

            var go = Object.Instantiate(chosen.prefab, hit.point, Quaternion.Euler(0f, yRot, 0f));
            go.transform.localScale = new Vector3(scaleX, scaleY, scaleZ);
            go.transform.SetParent(parent, worldPositionStays: true);
            RegisterPoint(hit.point, maxMinDistance, occupiedCells);
        }
    }

    private static float GetTotalWeight(
        BiomeData biome,
        System.Collections.Generic.Dictionary<BiomeData, float> cache)
    {
        if (cache.TryGetValue(biome, out float cachedWeight))
            return cachedWeight;

        float totalWeight = 0f;
        foreach (var p in biome.props)
            if (p?.prefab != null) totalWeight += p.weight;

        cache[biome] = totalWeight;
        return totalWeight;
    }

    private static WeightedPrefab PickWeightedRandom(
        WeightedPrefab[] props, float totalWeight, System.Random rng)
    {
        float roll       = (float)rng.NextDouble() * totalWeight;
        float cumulative = 0f;
        foreach (var p in props)
        {
            if (p?.prefab == null) continue;
            cumulative += p.weight;
            if (roll <= cumulative) return p;
        }
        return props[props.Length - 1];
    }

    private static float GetMaxMinDistance(BiomeData[] biomes)
    {
        float maxDistance = 0f;
        foreach (var biome in biomes)
        {
            if (biome?.props == null) continue;
            maxDistance = Mathf.Max(maxDistance, GetMaxMinDistance(biome.props));
        }

        return maxDistance;
    }

    private static float GetMaxMinDistance(WeightedPrefab[] props)
    {
        float maxDistance = 0f;
        foreach (var prop in props)
        {
            if (prop == null) continue;
            maxDistance = Mathf.Max(maxDistance, prop.minDistance);
        }

        return maxDistance;
    }

    private static bool IsTooClose(
        Vector3 point,
        float minDistance,
        float cellSize,
        System.Collections.Generic.Dictionary<Vector2Int, System.Collections.Generic.List<Vector3>> occupiedCells)
    {
        if (minDistance <= 0f || occupiedCells == null || cellSize <= 0f)
            return false;

        Vector2Int cell = ToCell(point, cellSize);
        float minDistanceSqr = minDistance * minDistance;

        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            if (!occupiedCells.TryGetValue(new Vector2Int(cell.x + x, cell.y + y), out var points))
                continue;

            foreach (var occupiedPoint in points)
            {
                Vector2 delta = new Vector2(point.x - occupiedPoint.x, point.z - occupiedPoint.z);
                if (delta.sqrMagnitude < minDistanceSqr)
                    return true;
            }
        }

        return false;
    }

    private static void RegisterPoint(
        Vector3 point,
        float cellSize,
        System.Collections.Generic.Dictionary<Vector2Int, System.Collections.Generic.List<Vector3>> occupiedCells)
    {
        if (occupiedCells == null || cellSize <= 0f)
            return;

        Vector2Int cell = ToCell(point, cellSize);
        if (!occupiedCells.TryGetValue(cell, out var points))
        {
            points = new System.Collections.Generic.List<Vector3>();
            occupiedCells[cell] = points;
        }

        points.Add(point);
    }

    private static Vector2Int ToCell(Vector3 point, float cellSize) => new Vector2Int(
        Mathf.FloorToInt(point.x / cellSize),
        Mathf.FloorToInt(point.z / cellSize));

    private static bool IsUnderwater(Vector3 point, WorldSettings settings)
    {
        if (settings == null)
            return false;

        return point.y <= settings.waterLevel + WaterSurfaceClearance;
    }
}
