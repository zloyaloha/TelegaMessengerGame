using UnityEngine;

[DisallowMultipleComponent]
public class CargoGridCellView : MonoBehaviour
{
    [SerializeField] private Vector3Int coords;
    [SerializeField] private Renderer targetRenderer;

    public Vector3Int Coords => coords;
    public Renderer TargetRenderer => targetRenderer;

    public void Initialize(Vector3Int newCoords, Renderer rendererComponent)
    {
        coords = newCoords;
        targetRenderer = rendererComponent;
    }
}
