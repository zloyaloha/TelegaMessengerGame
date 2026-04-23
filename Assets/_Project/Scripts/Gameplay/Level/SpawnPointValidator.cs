using System.Collections.Generic;
using UnityEngine;

public static class SpawnPointValidator
{
    private const float DefaultMinimumSurfaceNormalY = 0.7f;
    private const float DefaultWaterClearance = 0.25f;
    private const float DefaultProbeHeightOffset = 600f;
    private static readonly string[] DefaultBlockedBiomeKeywords = { "mountain", "water" };

    public static bool TryGetValidPoint(Vector3 center, float radius, out Vector3 result, int maxAttempts = 30)
    {
        return TryGetValidPoint(
            center,
            radius,
            out result,
            maxAttempts,
            DefaultMinimumSurfaceNormalY,
            DefaultWaterClearance,
            DefaultProbeHeightOffset,
            DefaultBlockedBiomeKeywords);
    }

    public static bool TryGetValidPoint(
        Vector3 center,
        float radius,
        out Vector3 result,
        int maxAttempts,
        float minimumSurfaceNormalY,
        float waterClearance,
        float probeHeightOffset,
        IReadOnlyList<string> blockedBiomeKeywords)
    {
        result = Vector3.zero;

        WorldGenerator worldGenerator = Object.FindFirstObjectByType<WorldGenerator>();
        WorldSettings worldSettings = worldGenerator != null ? worldGenerator.Settings : null;
        BiomeManager biomeManager = worldGenerator != null ? worldGenerator.BiomeManager : null;

        float safeRadius = Mathf.Max(0.1f, radius);
        int safeAttempts = Mathf.Max(1, maxAttempts);
        float minimumNormal = Mathf.Clamp(minimumSurfaceNormalY, 0.05f, 1f);
        float maximumSlopeAngle = Mathf.Acos(minimumNormal) * Mathf.Rad2Deg;

        for (int attempt = 0; attempt < safeAttempts; attempt++)
        {
            Vector2 sampleOffset = Random.insideUnitCircle * safeRadius;
            Vector3 candidate = center + new Vector3(sampleOffset.x, 0f, sampleOffset.y);

            if (!IsWithinWorldBounds(candidate, worldGenerator))
            {
                continue;
            }

            if (!TryProjectToTerrain(candidate, out Vector3 terrainPoint, probeHeightOffset))
            {
                continue;
            }

            if (terrainPoint.y <= GetWaterLevel(worldSettings, worldGenerator) + Mathf.Max(0f, waterClearance))
            {
                continue;
            }

            float slopeAngle = ResolveSlopeAngle(worldGenerator, candidate, terrainPoint);
            if (slopeAngle > maximumSlopeAngle)
            {
                continue;
            }

            if (IsBlockedBiome(candidate, terrainPoint.y, slopeAngle, biomeManager, worldSettings, blockedBiomeKeywords))
            {
                continue;
            }

            result = terrainPoint;
            return true;
        }

        return false;
    }

    public static bool TryProjectToTerrain(Vector3 worldPoint, out Vector3 terrainPoint, float probeHeightOffset = DefaultProbeHeightOffset)
    {
        terrainPoint = Vector3.zero;

        WorldGenerator worldGenerator = Object.FindFirstObjectByType<WorldGenerator>();
        WorldSettings worldSettings = worldGenerator != null ? worldGenerator.Settings : null;
        if (worldGenerator == null || worldSettings == null)
        {
            return TryRaycastTerrain(worldPoint, probeHeightOffset, null, out terrainPoint);
        }

        float sampledHeight = worldGenerator.transform.position.y + worldGenerator.SampleTerrainHeight(worldPoint.x, worldPoint.z);
        Vector3 sampledPoint = new Vector3(worldPoint.x, sampledHeight, worldPoint.z);

        if (TryRaycastTerrain(sampledPoint, probeHeightOffset, worldGenerator, out Vector3 raycastPoint))
        {
            terrainPoint = raycastPoint;
            return true;
        }

        terrainPoint = sampledPoint;
        return true;
    }

    private static bool IsWithinWorldBounds(Vector3 point, WorldGenerator worldGenerator)
    {
        if (worldGenerator == null)
        {
            return true;
        }

        Rect bounds = worldGenerator.WorldBoundsXZ;
        return bounds.width <= Mathf.Epsilon
            || bounds.height <= Mathf.Epsilon
            || bounds.Contains(new Vector2(point.x, point.z));
    }

    private static float GetWaterLevel(WorldSettings settings, WorldGenerator worldGenerator)
    {
        if (settings != null)
        {
            return settings.waterLevel + worldGenerator.transform.position.y;
        }

        return 0f;
    }

    private static float ResolveSlopeAngle(WorldGenerator worldGenerator, Vector3 samplePosition, Vector3 terrainPoint)
    {
        if (worldGenerator != null)
        {
            return worldGenerator.SampleTerrainSlope(samplePosition.x, samplePosition.z);
        }

        return 0f;
    }

    private static bool IsBlockedBiome(
        Vector3 samplePosition,
        float terrainHeight,
        float slopeAngle,
        BiomeManager biomeManager,
        WorldSettings worldSettings,
        IReadOnlyList<string> blockedBiomeKeywords)
    {
        if (biomeManager == null || blockedBiomeKeywords == null || blockedBiomeKeywords.Count == 0)
        {
            return false;
        }

        BiomeData biome = biomeManager.GetBiome(
            samplePosition.x,
            samplePosition.z,
            terrainHeight,
            slopeAngle,
            worldSettings);
        if (biome == null || string.IsNullOrWhiteSpace(biome.biomeName))
        {
            return false;
        }

        string biomeName = biome.biomeName.ToLowerInvariant();
        for (int i = 0; i < blockedBiomeKeywords.Count; i++)
        {
            string keyword = blockedBiomeKeywords[i];
            if (!string.IsNullOrWhiteSpace(keyword) && biomeName.Contains(keyword.ToLowerInvariant()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryRaycastTerrain(
        Vector3 worldPoint,
        float probeHeightOffset,
        WorldGenerator worldGenerator,
        out Vector3 terrainPoint)
    {
        terrainPoint = Vector3.zero;

        float safeProbeOffset = Mathf.Max(50f, probeHeightOffset);
        float rayOriginY = worldPoint.y + safeProbeOffset;
        float rayDistance = safeProbeOffset * 2f;
        if (worldGenerator != null && worldGenerator.Settings != null)
        {
            Vector2 terrainRange = worldGenerator.Settings.EvaluateHeightRange(0f, 1f);
            rayOriginY = worldGenerator.transform.position.y + terrainRange.y + safeProbeOffset;
            rayDistance = safeProbeOffset + Mathf.Abs(terrainRange.y - terrainRange.x) + 200f;
        }

        Vector3 rayOrigin = new Vector3(worldPoint.x, rayOriginY, worldPoint.z);
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            Mathf.Max(1f, rayDistance),
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        float targetHeight = worldGenerator != null && worldGenerator.Settings != null
            ? worldGenerator.transform.position.y + worldGenerator.SampleTerrainHeight(worldPoint.x, worldPoint.z)
            : worldPoint.y;

        bool foundMatch = false;
        float bestHeightDelta = float.PositiveInfinity;
        Vector3 bestPoint = Vector3.zero;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit candidate = hits[i];
            if (candidate.collider == null || candidate.collider.isTrigger)
            {
                continue;
            }

            if (worldGenerator == null)
            {
                terrainPoint = candidate.point;
                return true;
            }

            Transform candidateTransform = candidate.collider.transform;
            if (candidateTransform.parent == worldGenerator.transform
                && candidate.collider is MeshCollider
                && candidateTransform.TryGetComponent<MeshFilter>(out _))
            {
                float heightDelta = Mathf.Abs(candidate.point.y - targetHeight);
                if (!foundMatch || heightDelta < bestHeightDelta)
                {
                    bestHeightDelta = heightDelta;
                    bestPoint = candidate.point;
                    foundMatch = true;
                }
            }
        }

        if (!foundMatch)
        {
            return false;
        }

        terrainPoint = bestPoint;
        return true;
    }
}
