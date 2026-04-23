using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CartColliderSetup : MonoBehaviour
{
    private const float CargoContactOffset = 0.001f;

    [SerializeField] private BoxCollider floor;
    [SerializeField] private BoxCollider[] walls = Array.Empty<BoxCollider>();
    [SerializeField, Min(0.05f)] private float wallHeight = 0.6f;

    public BoxCollider Floor => floor;
    public BoxCollider[] Walls => walls;
    public float WallHeight => Mathf.Max(0.05f, wallHeight);

    private void Awake()
    {
        AutoAssignColliders();
        ValidateSetup();
        ApplyWallMaterial();
    }

    public Bounds GetCargoBounds()
    {
        return TryGetCargoBoundsLocal(transform, out Bounds localBounds)
            ? CargoGridPlacementUtility.TransformBounds(localBounds, transform.localToWorldMatrix)
            : new Bounds(transform.position, Vector3.zero);
    }

    public bool TryGetCargoBoundsLocal(Transform referenceSpace, out Bounds bounds)
    {
        bounds = default;

        AutoAssignColliders();
        if (floor == null)
        {
            return false;
        }

        if (!TryGetBoundsInCartSpace(floor, out Bounds floorBounds))
        {
            return false;
        }

        Vector3 min = floorBounds.min;
        Vector3 max = floorBounds.max;
        min.y = floorBounds.max.y;

        float resolvedHeight = Mathf.Max(0.05f, wallHeight);
        if (walls != null)
        {
            for (int i = 0; i < walls.Length; i++)
            {
                BoxCollider wall = walls[i];
                if (wall == null || !TryGetBoundsInCartSpace(wall, out Bounds wallBounds))
                {
                    continue;
                }

                bool thinOnX = wallBounds.size.x <= wallBounds.size.z;
                if (thinOnX)
                {
                    if (wallBounds.center.x >= 0f)
                    {
                        max.x = Mathf.Min(max.x, wallBounds.min.x);
                    }
                    else
                    {
                        min.x = Mathf.Max(min.x, wallBounds.max.x);
                    }
                }
                else
                {
                    if (wallBounds.center.z >= 0f)
                    {
                        max.z = Mathf.Min(max.z, wallBounds.min.z);
                    }
                    else
                    {
                        min.z = Mathf.Max(min.z, wallBounds.max.z);
                    }
                }

                resolvedHeight = Mathf.Max(resolvedHeight, wallBounds.max.y - min.y);
            }
        }

        max.y = min.y + resolvedHeight;
        Bounds cartLocalBounds = CreateBounds(min, max);

        if (referenceSpace == null || referenceSpace == transform)
        {
            bounds = cartLocalBounds;
            return true;
        }

        bounds = CargoGridPlacementUtility.TransformBounds(
            cartLocalBounds,
            referenceSpace.worldToLocalMatrix * transform.localToWorldMatrix);
        return true;
    }

    private void AutoAssignColliders()
    {
        if (floor == null)
        {
            floor = FindNamedCollider("CartBody");
        }

        if (walls == null || walls.Length == 0)
        {
            List<BoxCollider> resolvedWalls = new List<BoxCollider>(4);
            AddNamedColliderIfPresent(resolvedWalls, "LeftWall");
            AddNamedColliderIfPresent(resolvedWalls, "RightWall");
            AddNamedColliderIfPresent(resolvedWalls, "FrontWall");
            AddNamedColliderIfPresent(resolvedWalls, "BackWall");

            walls = resolvedWalls.ToArray();
        }

        if (walls != null && walls.Length > 0)
        {
            List<BoxCollider> validWalls = null;
            for (int i = 0; i < walls.Length; i++)
            {
                if (walls[i] != null)
                {
                    continue;
                }

                validWalls ??= new List<BoxCollider>(walls.Length);
                for (int j = 0; j < walls.Length; j++)
                {
                    if (walls[j] != null)
                    {
                        validWalls.Add(walls[j]);
                    }
                }

                break;
            }

            if (validWalls != null)
            {
                walls = validWalls.ToArray();
            }
        }

        if (wallHeight <= 0.05f)
        {
            wallHeight = ResolveWallHeight();
        }
    }

    private void ValidateSetup()
    {
        if (floor == null)
        {
            Debug.LogError($"Cart floor collider is not assigned on {name}.", this);
        }

        if (walls == null || walls.Length == 0)
        {
            Debug.LogError($"Cart wall colliders are not assigned on {name}.", this);
            return;
        }

        for (int i = 0; i < walls.Length; i++)
        {
            if (walls[i] == null)
            {
                Debug.LogError($"Cart wall collider slot {i} is missing on {name}.", this);
            }
        }
    }

    private void ApplyWallMaterial()
    {
        PhysicsMaterial material = CargoPhysicsSettings.Load().GetOrCreateCartMaterial();

        if (floor != null)
        {
            floor.sharedMaterial = material;
            floor.contactOffset = CargoContactOffset;
        }

        if (walls == null)
        {
            return;
        }

        for (int i = 0; i < walls.Length; i++)
        {
            if (walls[i] != null)
            {
                walls[i].sharedMaterial = material;
                walls[i].contactOffset = CargoContactOffset;
            }
        }
    }

    private bool TryGetBoundsInCartSpace(Collider colliderComponent, out Bounds bounds)
    {
        return CargoGridPlacementUtility.TryGetBoundsInSpace(
            colliderComponent.transform,
            new[] { colliderComponent },
            null,
            transform,
            out bounds);
    }

    private float ResolveWallHeight()
    {
        if (walls == null || walls.Length == 0)
        {
            return Mathf.Max(0.05f, wallHeight);
        }

        float highestWall = 0f;
        for (int i = 0; i < walls.Length; i++)
        {
            BoxCollider wall = walls[i];
            if (wall == null || !TryGetBoundsInCartSpace(wall, out Bounds wallBounds))
            {
                continue;
            }

            highestWall = Mathf.Max(highestWall, wallBounds.size.y);
        }

        return Mathf.Max(0.05f, highestWall);
    }

    private BoxCollider FindNamedCollider(string childName)
    {
        Transform child = transform.Find(childName);
        return child != null ? child.GetComponent<BoxCollider>() : null;
    }

    private void AddNamedColliderIfPresent(List<BoxCollider> colliders, string childName)
    {
        BoxCollider collider = FindNamedCollider(childName);
        if (collider != null)
        {
            colliders.Add(collider);
        }
    }

    private static Bounds CreateBounds(Vector3 min, Vector3 max)
    {
        Bounds bounds = new Bounds(min, Vector3.zero);
        bounds.Encapsulate(max);
        return bounds;
    }
}
