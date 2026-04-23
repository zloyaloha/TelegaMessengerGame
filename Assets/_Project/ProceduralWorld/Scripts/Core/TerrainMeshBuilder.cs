using UnityEngine;
using System.Collections.Generic;

public static class TerrainMeshBuilder
{
    // border: кольцо невидимых вершин снаружи чанка только для расчёта нормалей.
    // noiseMap должен быть размером (meshW + 2*border) x (meshH + 2*border).
    public static Mesh Build(float[,] noiseMap, WorldSettings settings, int border = 0)
    {
        int fullW = noiseMap.GetLength(0);
        int fullH = noiseMap.GetLength(1);
        int meshW = fullW - 2 * border;
        int meshH = fullH - 2 * border;

        // Карта индексов: реальные вершины >= 0, бордерные < 0
        int[,] indexMap = new int[fullW, fullH];
        int meshIdx      = 0;
        int borderIdx    = -1;

        for (int y = 0; y < fullH; y++)
        for (int x = 0; x < fullW; x++)
        {
            bool isBorder = border > 0 &&
                            (x < border || x >= fullW - border ||
                             y < border || y >= fullH - border);
            indexMap[x, y] = isBorder ? borderIdx-- : meshIdx++;
        }

        int borderVertCount = -borderIdx - 1;
        Vector3[] vertices       = new Vector3[meshW * meshH];
        Vector3[] borderVertices = new Vector3[borderVertCount];
        Vector2[] uvs            = new Vector2[meshW * meshH];

        var tris       = new List<int>();
        var borderTris = new List<int>();

        // Заполняем позиции вершин
        for (int y = 0; y < fullH; y++)
        for (int x = 0; x < fullW; x++)
        {
            int   idx    = indexMap[x, y];
            int   lx     = x - border;
            int   ly     = y - border;
            float height = settings.EvaluateHeight(noiseMap[x, y]);
            if (float.IsNaN(height) || float.IsInfinity(height))
                height = 0f;
            // meshScale растягивает мир по XZ; Y (высота) не трогается,
            // чтобы не ломать нормализованные цветовые слои шейдера
            Vector3 pos  = new Vector3(lx * settings.meshScale, height, ly * settings.meshScale);

            if (idx < 0)
                borderVertices[-idx - 1] = pos;
            else
            {
                vertices[idx] = pos;
                float safeWidth = Mathf.Max(1f, meshW - 1);
                float safeHeight = Mathf.Max(1f, meshH - 1);
                uvs[idx] = new Vector2(lx / safeWidth, ly / safeHeight);
            }
        }

        // Строим треугольники; бордерные — в отдельный список
        for (int y = 0; y < fullH - 1; y++)
        for (int x = 0; x < fullW - 1; x++)
        {
            int a = indexMap[x,     y    ]; // top-left
            int b = indexMap[x + 1, y    ]; // top-right
            int c = indexMap[x,     y + 1]; // bottom-left
            int d = indexMap[x + 1, y + 1]; // bottom-right

            // tri1: a, c, d
            var l1 = (a < 0 || c < 0 || d < 0) ? borderTris : tris;
            l1.Add(a); l1.Add(c); l1.Add(d);

            // tri2: a, d, b
            var l2 = (a < 0 || d < 0 || b < 0) ? borderTris : tris;
            l2.Add(a); l2.Add(d); l2.Add(b);
        }

        int[] triangles       = tris.ToArray();
        int[] borderTriangles = borderTris.ToArray();

        // Считаем нормали: бордерные треугольники добавляют вклад только реальным вершинам
        Vector3[] normals = CalculateNormals(vertices, borderVertices, triangles, borderTriangles);

        Mesh mesh = new Mesh();
        if (vertices.Length > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices  = vertices;
        mesh.triangles = triangles;
        mesh.uv        = uvs;
        mesh.normals   = normals;
        return mesh;
    }

    private static Vector3[] CalculateNormals(
        Vector3[] verts, Vector3[] borderVerts,
        int[] tris, int[] borderTris)
    {
        Vector3[] normals = new Vector3[verts.Length];

        // Внутренние треугольники — вклад всем трём вершинам
        for (int i = 0; i < tris.Length; i += 3)
        {
            int a = tris[i], b = tris[i + 1], c = tris[i + 2];
            Vector3 n = SurfaceNormal(a, b, c, verts, borderVerts);
            normals[a] += n;
            normals[b] += n;
            normals[c] += n;
        }

        // Бордерные треугольники — вклад только реальным вершинам (index >= 0)
        for (int i = 0; i < borderTris.Length; i += 3)
        {
            int a = borderTris[i], b = borderTris[i + 1], c = borderTris[i + 2];
            Vector3 n = SurfaceNormal(a, b, c, verts, borderVerts);
            if (a >= 0) normals[a] += n;
            if (b >= 0) normals[b] += n;
            if (c >= 0) normals[c] += n;
        }

        for (int i = 0; i < normals.Length; i++)
            normals[i] = normals[i].normalized;

        return normals;
    }

    private static Vector3 SurfaceNormal(int a, int b, int c, Vector3[] verts, Vector3[] borderVerts)
    {
        Vector3 pA = a < 0 ? borderVerts[-a - 1] : verts[a];
        Vector3 pB = b < 0 ? borderVerts[-b - 1] : verts[b];
        Vector3 pC = c < 0 ? borderVerts[-c - 1] : verts[c];
        return Vector3.Cross(pB - pA, pC - pA).normalized;
    }
}
