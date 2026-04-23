using UnityEngine;

public struct LevelResult
{
    public int stars;
    public int deliveredCargoCount;
    public int totalCargoCount;
    public float cargoDeliveredPercent;
    public float averageCargoHpPercent;
    public float cartHpPercent;
    public float finalScore;

    public LevelResult(
        int stars,
        int deliveredCargoCount,
        int totalCargoCount,
        float cargoDeliveredPercent,
        float averageCargoHpPercent,
        float cartHpPercent,
        float finalScore)
    {
        this.stars = stars;
        this.deliveredCargoCount = deliveredCargoCount;
        this.totalCargoCount = totalCargoCount;
        this.cargoDeliveredPercent = cargoDeliveredPercent;
        this.averageCargoHpPercent = averageCargoHpPercent;
        this.cartHpPercent = cartHpPercent;
        this.finalScore = finalScore;
    }
}
