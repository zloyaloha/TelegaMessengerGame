using UnityEngine;

/// <summary>
/// Контракт поставщика карты высот для одного чанка.
/// Позволяет менять алгоритм генерации без изменения ChunkGenerator.
/// </summary>
public interface ITerrainProvider
{
    /// <summary>
    /// Возвращает heightmap размером (width + 2*border) × (height + 2*border).
    /// Бордерные пиксели нужны только для расчёта нормалей на краях чанка.
    /// </summary>
    float[,] GetHeightMap(Vector2Int chunkCoord, WorldSettings settings, int border);
}
