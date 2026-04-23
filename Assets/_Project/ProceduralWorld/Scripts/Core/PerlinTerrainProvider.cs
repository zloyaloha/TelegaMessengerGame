using UnityEngine;

/// <summary>
/// Реализация ITerrainProvider на основе многооктавного шума Перлина.
/// Создаётся как ScriptableObject-ассет и подключается к WorldGenerator через инспектор.
/// </summary>
[CreateAssetMenu(fileName = "PerlinTerrainProvider",
                 menuName   = "ProceduralWorld/Perlin Terrain Provider")]
public class PerlinTerrainProvider : ScriptableObject, ITerrainProvider
{
    public float[,] GetHeightMap(Vector2Int chunkCoord, WorldSettings settings, int border)
    {
        // Мировой origin чанка (в единицах вершин)
        Vector2 noiseOffset = new Vector2(
            chunkCoord.x * (settings.chunkWidth  - 1),
            chunkCoord.y * (settings.chunkHeight - 1));

        // Расширяем на border с каждой стороны для правильного расчёта нормалей
        Vector2 extendedOrigin = noiseOffset - new Vector2(border, border);
        int nW = settings.chunkWidth  + 2 * border;
        int nH = settings.chunkHeight + 2 * border;

        return NoiseGenerator.Generate(settings, extendedOrigin, nW, nH);
    }
}
