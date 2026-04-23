using UnityEngine;

[CreateAssetMenu(fileName = "CargoData", menuName = "Gameplay/Cargo/Cargo Data")]
public class CargoData : ScriptableObject
{
    [SerializeField] private string cargoName = "Cargo";
    [SerializeField] private Vector3Int gridSize = Vector3Int.one;
    [SerializeField, Min(0f)] private float weight = 1f;
    [SerializeField] private GameObject prefab;
    [SerializeField] private Sprite icon;

    public string CargoName => string.IsNullOrWhiteSpace(cargoName) ? name : cargoName;
    public Vector3Int GridSize => new Vector3Int(
        Mathf.Max(1, gridSize.x),
        Mathf.Max(1, gridSize.y),
        Mathf.Max(1, gridSize.z));
    public float Weight => Mathf.Max(0f, weight);
    public GameObject Prefab => prefab;
    public Sprite Icon => icon;

    private void OnValidate()
    {
        gridSize.x = Mathf.Max(1, gridSize.x);
        gridSize.y = Mathf.Max(1, gridSize.y);
        gridSize.z = Mathf.Max(1, gridSize.z);
        weight = Mathf.Max(0f, weight);
    }
}
