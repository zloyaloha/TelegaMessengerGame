using System.Collections.Generic;
using UnityEngine;

public static class ScoringSystem
{
    public static LevelResult CalculateScore(
        LevelConfig config,
        int totalCargo,
        List<CargoInstance> deliveredCargo,
        float cartHpPercent)
    {
        int safeTotalCargo = Mathf.Max(0, totalCargo);
        int deliveredCount = deliveredCargo != null ? deliveredCargo.Count : 0;

        float cargoDeliveredPercent = safeTotalCargo <= 0
            ? 0f
            : Mathf.Clamp01(deliveredCount / (float)safeTotalCargo);

        float averageCargoHpPercent = 0f;
        if (deliveredCargo != null && deliveredCargo.Count > 0)
        {
            float hpSum = 0f;
            int hpCount = 0;
            for (int i = 0; i < deliveredCargo.Count; i++)
            {
                CargoInstance cargo = deliveredCargo[i];
                if (cargo == null)
                {
                    continue;
                }

                hpSum += cargo.HpPercent;
                hpCount++;
            }

            averageCargoHpPercent = hpCount > 0 ? Mathf.Clamp01(hpSum / hpCount) : 0f;
        }

        float clampedCartHp = Mathf.Clamp01(cartHpPercent);
        float cargoScore = (cargoDeliveredPercent * 0.5f) + (averageCargoHpPercent * 0.5f);
        float weight = config != null ? Mathf.Clamp01(config.cartHpWeight) : 0f;
        float finalScore = Mathf.Clamp01((cargoScore * (1f - weight)) + (clampedCartHp * weight));

        int stars = 1;
        if (config != null)
        {
            float threeStarThreshold = BuildFinalThreshold(
                config.threeStarMinCargo,
                config.threeStarMinHp,
                weight);
            float twoStarThreshold = BuildFinalThreshold(
                config.twoStarMinCargo,
                config.twoStarMinHp,
                weight);

            bool qualifiesForThree = cargoDeliveredPercent >= config.threeStarMinCargo
                && averageCargoHpPercent >= config.threeStarMinHp
                && finalScore >= threeStarThreshold;

            bool qualifiesForTwo = cargoDeliveredPercent >= config.twoStarMinCargo
                && averageCargoHpPercent >= config.twoStarMinHp
                && finalScore >= twoStarThreshold;

            stars = qualifiesForThree ? 3 : (qualifiesForTwo ? 2 : 1);
        }
        else
        {
            stars = finalScore >= 0.8f ? 3 : (finalScore >= 0.5f ? 2 : 1);
        }

        return new LevelResult(
            stars,
            deliveredCount,
            safeTotalCargo,
            cargoDeliveredPercent,
            averageCargoHpPercent,
            clampedCartHp,
            finalScore);
    }

    private static float BuildFinalThreshold(float minimumCargoPercent, float minimumHpPercent, float cartHpWeight)
    {
        float cargoThreshold = (Mathf.Clamp01(minimumCargoPercent) + Mathf.Clamp01(minimumHpPercent)) * 0.5f;
        return Mathf.Clamp01((cargoThreshold * (1f - cartHpWeight)) + (Mathf.Clamp01(minimumHpPercent) * cartHpWeight));
    }
}
