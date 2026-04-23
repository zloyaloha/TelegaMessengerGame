using UnityEngine;

[System.Serializable]
public struct BiomeSampleContext
{
    public float height;
    public float temperature;
    public float slope;

    public BiomeSampleContext(float height, float temperature, float slope = 0f)
    {
        this.height = height;
        this.temperature = temperature;
        this.slope = slope;
    }
}

/// <summary>
/// Базовый класс узла булева условия для биома.
/// Листовые узлы — диапазонные предикаты, составные — AND / OR / NOT.
/// Добавить новый тип условия: унаследовать от BiomeConditionNode,
/// пометить [System.Serializable] и зарегистрировать в BiomeDataEditor.CondTypes.
/// </summary>
[System.Serializable]
public abstract class BiomeConditionNode
{
    /// <summary>Возвращает true, если условие выполнено.</summary>
    public abstract bool Evaluate(BiomeSampleContext sample);

    /// <summary>
    /// «Мягкое расстояние» до выполнения условия:
    /// 0 — условие выполнено, >0 — насколько далеко.
    /// Используется BiomeManager для выбора «ближайшего» биома,
    /// когда ни один не удовлетворяет условию точно.
    /// </summary>
    public abstract float Score(BiomeSampleContext sample);

    public bool Evaluate(float height, float temperature) =>
        Evaluate(new BiomeSampleContext(height, temperature));

    public float Score(float height, float temperature) =>
        Score(new BiomeSampleContext(height, temperature));
}

// ─── Листовые узлы ──────────────────────────────────────────────────────────

/// <summary>Условие: высота в диапазоне [min, max] (в метрах).</summary>
[System.Serializable]
public class HeightRangeCondition : BiomeConditionNode
{
    [Tooltip("Минимальная высота (м)")]
    public float min = 0f;
    [Tooltip("Максимальная высота (м)")]
    public float max = 20f;

    public override bool Evaluate(BiomeSampleContext sample) =>
        sample.height >= min && sample.height <= max;

    public override float Score(BiomeSampleContext sample)
    {
        if (sample.height < min) return min - sample.height;
        if (sample.height > max) return sample.height - max;
        return 0f;
    }
}

/// <summary>Условие: температурный шум в диапазоне [min, max] (0–1).</summary>
[System.Serializable]
public class TemperatureRangeCondition : BiomeConditionNode
{
    [Range(0f, 1f), Tooltip("Минимальная температура (0–1)")]
    public float min = 0f;
    [Range(0f, 1f), Tooltip("Максимальная температура (0–1)")]
    public float max = 1f;

    public override bool Evaluate(BiomeSampleContext sample) =>
        sample.temperature >= min && sample.temperature <= max;

    public override float Score(BiomeSampleContext sample)
    {
        // ×100 чтобы температура и высота давали сопоставимые штрафы
        if (sample.temperature < min) return (min - sample.temperature) * 100f;
        if (sample.temperature > max) return (sample.temperature - max) * 100f;
        return 0f;
    }
}

// ─── Составные узлы ─────────────────────────────────────────────────────────

/// <summary>AND: все дочерние условия должны быть выполнены.</summary>
[System.Serializable]
public class AndCondition : BiomeConditionNode
{
    [SerializeReference]
    public BiomeConditionNode[] children = new BiomeConditionNode[0];

    public override bool Evaluate(BiomeSampleContext sample)
    {
        if (children == null || children.Length == 0) return true;
        foreach (var c in children)
            if (c == null || !c.Evaluate(sample)) return false;
        return true;
    }

    public override float Score(BiomeSampleContext sample)
    {
        if (children == null || children.Length == 0) return 0f;
        float total = 0f;
        foreach (var c in children)
            if (c != null) total += c.Score(sample);
        return total;
    }
}

/// <summary>OR: хотя бы одно дочернее условие должно быть выполнено.</summary>
[System.Serializable]
public class OrCondition : BiomeConditionNode
{
    [SerializeReference]
    public BiomeConditionNode[] children = new BiomeConditionNode[0];

    public override bool Evaluate(BiomeSampleContext sample)
    {
        if (children == null || children.Length == 0) return false;
        foreach (var c in children)
            if (c != null && c.Evaluate(sample)) return true;
        return false;
    }

    public override float Score(BiomeSampleContext sample)
    {
        if (children == null || children.Length == 0) return 1000f;
        float best = float.MaxValue;
        foreach (var c in children)
            if (c != null) best = Mathf.Min(best, c.Score(sample));
        return best == float.MaxValue ? 1000f : best;
    }
}

/// <summary>NOT: инвертирует дочернее условие.</summary>
[System.Serializable]
public class NotCondition : BiomeConditionNode
{
    [SerializeReference]
    public BiomeConditionNode child;

    public override bool Evaluate(BiomeSampleContext sample) =>
        child != null && !child.Evaluate(sample);

    public override float Score(BiomeSampleContext sample)
    {
        if (child == null) return 0f;
        // child выполнен (score=0) → NOT ложно → большой штраф
        // child не выполнен (score>0) → NOT истинно → 0
        return child.Score(sample) == 0f ? 1000f : 0f;
    }
}
