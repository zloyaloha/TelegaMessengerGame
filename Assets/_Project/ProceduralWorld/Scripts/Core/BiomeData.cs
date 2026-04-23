using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Данные одного биома: условия появления, цветовые слои, объекты для размещения.
/// Создаётся через Assets → Create → ProceduralWorld → Biome Data.
/// </summary>

[System.Serializable]
public class WeightedPrefab
{
    public GameObject prefab;
    [Min(0f)] public float weight = 1f;
    [Range(0f, 1f)] public float spawnChance = 1f;
    [Min(0f)] public float minDistance = 0f;
    [Range(0f, 90f)] public float maxSlopeAngle = 30f;
    public Vector3 scaleMin = Vector3.one;
    public Vector3 scaleMax = Vector3.one;
}

[CreateAssetMenu(fileName = "BiomeData", menuName = "ProceduralWorld/Biome Data")]
public class BiomeData : ScriptableObject
{
    [Header("Идентификация")]
    public string biomeName = "Unnamed";
    [FormerlySerializedAs("mapColor")]
    public Color minimapColor = Color.green;

    public Color mapColor
    {
        get => minimapColor;
        set => minimapColor = value;
    }

    [Header("Тонирование террейна")]
    [Tooltip("Мультипликативный тинт поверх высотных цветов. Белый = без изменений.")]
    public Color terrainTint = Color.white;

    [Header("Текстура земли")]
    public Texture2D groundTexture;
    [Min(0.1f)] public float groundTextureWorldSize = 24f;

    [Header("Приоритет выбора")]
    [Tooltip("Если несколько биомов подходят одновременно, сначала выбирается биом с более высоким приоритетом.")]
    public int selectionPriority = 0;

    [Header("Условие появления биома (булева алгебра)")]
    [Tooltip("Дерево булевых условий: AND/OR/NOT + диапазоны высоты и температуры.")]
    [SerializeReference]
    public BiomeConditionNode condition;

    [FormerlySerializedAs("minHeight")]
    [SerializeField, HideInInspector] private float legacyMinHeight = 0f;
    [FormerlySerializedAs("maxHeight")]
    [SerializeField, HideInInspector] private float legacyMaxHeight = 1f;
    [FormerlySerializedAs("minTemperature")]
    [SerializeField, HideInInspector] private float legacyMinTemperature = 0f;
    [FormerlySerializedAs("maxTemperature")]
    [SerializeField, HideInInspector] private float legacyMaxTemperature = 1f;

    [Header("Объекты биома")]
    public WeightedPrefab[] props;
    [Range(0f, 0.5f)] public float propDensity = 0.02f;

    [System.NonSerialized] private BiomeConditionNode _resolvedLegacyCondition;
    [System.NonSerialized] private int _resolvedLegacyHash;

    public BiomeConditionNode GetResolvedCondition(WorldSettings settings)
    {
        if (condition != null)
            return condition;

        if (!HasLegacyConditionData())
            return null;

        int currentHash = BuildLegacyHash(settings);
        if (_resolvedLegacyCondition != null && _resolvedLegacyHash == currentHash)
            return _resolvedLegacyCondition;

        Vector2 heightRange = settings != null
            ? settings.EvaluateHeightRange(legacyMinHeight, legacyMaxHeight)
            : new Vector2(legacyMinHeight, legacyMaxHeight);

        _resolvedLegacyCondition = new AndCondition
        {
            children = new BiomeConditionNode[]
            {
                new HeightRangeCondition
                {
                    min = Mathf.Min(heightRange.x, heightRange.y),
                    max = Mathf.Max(heightRange.x, heightRange.y),
                },
                new TemperatureRangeCondition
                {
                    min = Mathf.Clamp01(Mathf.Min(legacyMinTemperature, legacyMaxTemperature)),
                    max = Mathf.Clamp01(Mathf.Max(legacyMinTemperature, legacyMaxTemperature)),
                },
            }
        };

        _resolvedLegacyHash = currentHash;
        return _resolvedLegacyCondition;
    }

    public bool HasLegacyConditionData() =>
        legacyMinHeight != 0f ||
        legacyMaxHeight != 1f ||
        legacyMinTemperature != 0f ||
        legacyMaxTemperature != 1f;

    private int BuildLegacyHash(WorldSettings settings)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + legacyMinHeight.GetHashCode();
            hash = hash * 31 + legacyMaxHeight.GetHashCode();
            hash = hash * 31 + legacyMinTemperature.GetHashCode();
            hash = hash * 31 + legacyMaxTemperature.GetHashCode();

            if (settings != null)
            {
                hash = hash * 31 + settings.heightMultiplier.GetHashCode();

                var curve = settings.heightCurve;
                if (curve != null)
                {
                    var keys = curve.keys;
                    hash = hash * 31 + keys.Length;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        hash = hash * 31 + keys[i].time.GetHashCode();
                        hash = hash * 31 + keys[i].value.GetHashCode();
                        hash = hash * 31 + keys[i].inTangent.GetHashCode();
                        hash = hash * 31 + keys[i].outTangent.GetHashCode();
                    }
                }
            }

            return hash;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        _resolvedLegacyCondition = null;
        _resolvedLegacyHash = 0;
        groundTextureWorldSize = Mathf.Max(0.1f, groundTextureWorldSize);
        SanitizeProps();
        WorldGeneratorRefreshScheduler.RequestAll(WorldGeneratorRefreshMode.BiomesOnly);
    }

    private void SanitizeProps()
    {
        if (props == null) return;

        for (int i = 0; i < props.Length; i++)
        {
            var prop = props[i];
            if (prop == null) continue;

            prop.weight = Mathf.Max(0f, prop.weight);
            prop.spawnChance = Mathf.Clamp01(prop.spawnChance);
            prop.minDistance = Mathf.Max(0f, prop.minDistance);
            prop.maxSlopeAngle = Mathf.Clamp(prop.maxSlopeAngle, 0f, 90f);

            if (prop.scaleMin == Vector3.zero && prop.scaleMax == Vector3.zero)
            {
                prop.scaleMin = Vector3.one;
                prop.scaleMax = Vector3.one;
            }
            else
            {
                prop.scaleMin = MaxVector(prop.scaleMin, Vector3.one * 0.01f);
                prop.scaleMax = MaxVector(prop.scaleMax, Vector3.one * 0.01f);
            }

            prop.scaleMin = Vector3.Min(prop.scaleMin, prop.scaleMax);
            prop.scaleMax = Vector3.Max(prop.scaleMax, prop.scaleMin);
        }
    }

    private static Vector3 MaxVector(Vector3 value, Vector3 min) => new Vector3(
        Mathf.Max(value.x, min.x),
        Mathf.Max(value.y, min.y),
        Mathf.Max(value.z, min.z));
#endif
}
