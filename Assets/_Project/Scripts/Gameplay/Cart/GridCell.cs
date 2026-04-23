using UnityEngine;

[System.Serializable]
public class GridCell
{
    public Vector3Int coords;
    public bool isOccupied;
    public CargoInstance occupiedBy;

    public GridCell(Vector3Int coords)
    {
        this.coords = coords;
    }
}
