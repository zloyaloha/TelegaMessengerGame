using UnityEngine;

/// <summary>
/// Строит меш одного чанка. Принимает поставщика карты высот через ITerrainProvider.
/// Если поставщик не задан — использует NoiseGenerator напрямую (обратная совместимость).
/// Если передан BiomeManager — записывает тинт биома в mesh.colors
/// и индекс биома в uv2 для текстурного шейдера.
/// </summary>
public static class ChunkGenerator
{
    public static Mesh BuildChunk(
        Vector2Int       chunkCoord,
        WorldSettings    settings,
        ITerrainProvider provider      = null,
        BiomeManager     biomeManager  = null)
    {
        const int border = 1;
        float[,] noiseMap;

        if (provider != null)
        {
            noiseMap = provider.GetHeightMap(chunkCoord, settings, border);
        }
        else
        {
            // Fallback для обратной совместимости
            Vector2 noiseOffset = new Vector2(
                chunkCoord.x * (settings.chunkWidth  - 1),
                chunkCoord.y * (settings.chunkHeight - 1));
            Vector2 extendedOrigin = noiseOffset - new Vector2(border, border);
            noiseMap = NoiseGenerator.Generate(settings, extendedOrigin,
                settings.chunkWidth + 2 * border, settings.chunkHeight + 2 * border);
        }

        Mesh mesh = TerrainMeshBuilder.Build(noiseMap, settings, border);

        int vertexCount = mesh.vertexCount;
        if (vertexCount == 0)
            return mesh;

        Color[] colors = new Color[vertexCount];
        Vector2[] biomeData = new Vector2[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            colors[i] = Color.white;
            biomeData[i] = Vector2.zero;
        }

        if (biomeManager != null)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            float chunkOriginX = chunkCoord.x * (settings.chunkWidth - 1) * settings.meshScale;
            float chunkOriginZ = chunkCoord.y * (settings.chunkHeight - 1) * settings.meshScale;

            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 vertex = vertices[i];
                float worldX = chunkOriginX + vertex.x;
                float worldZ = chunkOriginZ + vertex.z;
                float slope = normals != null && i < normals.Length
                    ? Vector3.Angle(normals[i], Vector3.up)
                    : 0f;

                BiomeData biome = biomeManager.GetBiome(worldX, worldZ, vertex.y, slope, settings);
                colors[i] = biome != null ? biome.terrainTint : Color.white;
                biomeData[i] = new Vector2(
                    biome != null ? biomeManager.GetBiomeIndex(biome) : 0f,
                    Mathf.Clamp01(slope / 90f));
            }
        }

        mesh.colors = colors;
        mesh.uv2 = biomeData;

        return mesh;
    }
}
