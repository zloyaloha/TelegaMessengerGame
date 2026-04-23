using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(WorldGenerator))]
public class WorldGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        WorldGenerator generator = (WorldGenerator)target;

        EditorGUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "Изменения в WorldSettings, BiomeManager и BiomeData теперь обновляют сцену автоматически. " +
            "Кнопки ниже полезны для ручного полного или биомного refresh.",
            MessageType.Info);

        if (GUILayout.Button("Полная генерация мира", GUILayout.Height(35)))
            generator.GenerateWorld();

        if (GUILayout.Button("Обновить биомы и пропсы", GUILayout.Height(25)))
            generator.RefreshBiomes();

        if (GUILayout.Button("Очистить", GUILayout.Height(25)))
            generator.ClearWorld();
    }
}
