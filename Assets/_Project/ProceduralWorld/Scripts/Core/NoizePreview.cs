using UnityEngine;

[ExecuteInEditMode]
public class NoisePreview : MonoBehaviour {
    public WorldSettings settings;

    [Header("Режим")]
    public bool show3D = false;

    private void OnValidate()
    {
        if (settings != null)
            UpdatePreview();
    }

    public void UpdatePreview()
    {
        float[,] noiseMap = NoiseGenerator.Generate(settings);

        if (show3D)
            ApplyMesh(noiseMap);
        else
            ApplyTexture(noiseMap);
    }

    private void ApplyTexture(float[,] map)
    {
        int width = settings.chunkWidth;
        int height = settings.chunkHeight;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float v = map[x, y];
                pixels[y * width + x] = new Color(v, v, v);
            }

        tex.SetPixels(pixels);
        tex.Apply();

        GetComponent<MeshRenderer>().sharedMaterial.mainTexture = tex;
    }


    private void ApplyMesh(float[,] noiseMap)
    {
        Mesh mesh = TerrainMeshBuilder.Build(noiseMap, settings);

        GetComponent<MeshFilter>().sharedMesh = mesh;

        MeshCollider col = GetComponent<MeshCollider>();
        if (col != null)
            col.sharedMesh = mesh;

    }
}