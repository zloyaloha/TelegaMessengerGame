using UnityEngine;

[CreateAssetMenu(fileName = "LevelConfig", menuName = "Gameplay/Levels/Level Config")]
public class LevelConfig : ScriptableObject
{
    public string levelName = "Level";
    [Min(1)] public int cargoCount = 3;
    public CargoData[] possibleCargoTypes;
    [Min(5f)] public float deliveryDistance = 100f;
    [Min(0.5f)] public float deliveryRadius = 5f;

    [Header("Scoring")]
    [Range(0f, 1f)] public float threeStarMinCargo = 0.9f;
    [Range(0f, 1f)] public float twoStarMinCargo = 0.7f;
    [Range(0f, 1f)] public float threeStarMinHp = 0.8f;
    [Range(0f, 1f)] public float twoStarMinHp = 0.5f;
    [Range(0f, 1f)] public float cartHpWeight = 0.3f;

    private void OnValidate()
    {
        cargoCount = Mathf.Max(1, cargoCount);
        deliveryDistance = Mathf.Max(5f, deliveryDistance);
        deliveryRadius = Mathf.Max(0.5f, deliveryRadius);

        threeStarMinCargo = Mathf.Clamp01(threeStarMinCargo);
        twoStarMinCargo = Mathf.Clamp01(twoStarMinCargo);
        threeStarMinHp = Mathf.Clamp01(threeStarMinHp);
        twoStarMinHp = Mathf.Clamp01(twoStarMinHp);
        cartHpWeight = Mathf.Clamp01(cartHpWeight);

        if (twoStarMinCargo > threeStarMinCargo)
        {
            twoStarMinCargo = threeStarMinCargo;
        }

        if (twoStarMinHp > threeStarMinHp)
        {
            twoStarMinHp = threeStarMinHp;
        }
    }
}
