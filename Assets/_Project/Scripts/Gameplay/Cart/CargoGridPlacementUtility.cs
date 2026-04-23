using UnityEngine;

public static class CargoGridPlacementUtility
{
    public static bool TryCalculatePlacement(
        Transform root,
        Collider[] colliders,
        Renderer[] renderers,
        Transform parent,
        Vector3 blockMinLocal,
        Vector3 fallbackLocalScale,
        out Vector3 localPosition,
        out Vector3 localScale)
    {
        localPosition = blockMinLocal;
        localScale = fallbackLocalScale;

        if (root == null || parent == null || !TryGetLocalBounds(root, colliders, renderers, out Bounds localBounds))
        {
            return false;
        }

        // Grid occupancy is defined by gridSize/cell layout, not by stretching the authored cargo prefab.
        localScale = fallbackLocalScale;
        localPosition = blockMinLocal - Vector3.Scale(localBounds.min, localScale);
        return true;
    }

    public static bool TryGetBoundsInSpace(
        Transform root,
        Collider[] colliders,
        Renderer[] renderers,
        Transform referenceSpace,
        out Bounds bounds)
    {
        bounds = default;

        if (root == null || !TryGetLocalBounds(root, colliders, renderers, out Bounds localBounds))
        {
            return false;
        }

        Matrix4x4 referenceMatrix = referenceSpace != null
            ? referenceSpace.worldToLocalMatrix * root.localToWorldMatrix
            : root.localToWorldMatrix;

        bounds = TransformBounds(localBounds, referenceMatrix);
        return true;
    }

    public static bool TryGetLocalBounds(Transform root, Collider[] colliders, Renderer[] renderers, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        if (colliders != null)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null
                    || !colliders[i].enabled
                    || !colliders[i].gameObject.activeInHierarchy
                    || colliders[i].isTrigger)
                {
                    continue;
                }

                if (TryGetColliderBounds(colliders[i], out Bounds colliderBounds))
                {
                    AppendBounds(root, colliders[i].transform, colliderBounds, ref bounds, ref hasBounds);
                }
            }
        }

        if (hasBounds || renderers == null)
        {
            return hasBounds;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer rendererComponent = renderers[i];
            if (rendererComponent == null
                || !rendererComponent.enabled
                || !rendererComponent.gameObject.activeInHierarchy)
            {
                continue;
            }

            AppendBounds(root, rendererComponent.transform, rendererComponent.localBounds, ref bounds, ref hasBounds);
        }

        return hasBounds;
    }

    public static Bounds TransformBounds(Bounds sourceBounds, Matrix4x4 matrix)
    {
        Vector3 extents = sourceBounds.extents;
        Vector3 center = sourceBounds.center;

        Vector3[] corners =
        {
            center + new Vector3(-extents.x, -extents.y, -extents.z),
            center + new Vector3(-extents.x, -extents.y, extents.z),
            center + new Vector3(-extents.x, extents.y, -extents.z),
            center + new Vector3(-extents.x, extents.y, extents.z),
            center + new Vector3(extents.x, -extents.y, -extents.z),
            center + new Vector3(extents.x, -extents.y, extents.z),
            center + new Vector3(extents.x, extents.y, -extents.z),
            center + new Vector3(extents.x, extents.y, extents.z)
        };

        Bounds transformedBounds = new Bounds(matrix.MultiplyPoint3x4(corners[0]), Vector3.zero);
        for (int i = 1; i < corners.Length; i++)
        {
            transformedBounds.Encapsulate(matrix.MultiplyPoint3x4(corners[i]));
        }

        return transformedBounds;
    }

    private static bool TryGetColliderBounds(Collider colliderComponent, out Bounds bounds)
    {
        bounds = default;

        switch (colliderComponent)
        {
            case BoxCollider boxCollider:
                bounds = new Bounds(boxCollider.center, boxCollider.size);
                return true;

            case SphereCollider sphereCollider:
                float sphereDiameter = sphereCollider.radius * 2f;
                bounds = new Bounds(sphereCollider.center, Vector3.one * sphereDiameter);
                return true;

            case CapsuleCollider capsuleCollider:
                float capsuleDiameter = capsuleCollider.radius * 2f;
                Vector3 capsuleSize = Vector3.one * capsuleDiameter;
                capsuleSize[capsuleCollider.direction] = Mathf.Max(capsuleCollider.height, capsuleDiameter);
                bounds = new Bounds(capsuleCollider.center, capsuleSize);
                return true;

            case MeshCollider meshCollider when meshCollider.sharedMesh != null:
                bounds = meshCollider.sharedMesh.bounds;
                return true;

            default:
                return false;
        }
    }

    private static void AppendBounds(
        Transform root,
        Transform sourceTransform,
        Bounds sourceBounds,
        ref Bounds combinedBounds,
        ref bool hasBounds)
    {
        Matrix4x4 toRootMatrix = root.worldToLocalMatrix * sourceTransform.localToWorldMatrix;
        Bounds transformedBounds = TransformBounds(sourceBounds, toRootMatrix);
        if (!hasBounds)
        {
            combinedBounds = transformedBounds;
            hasBounds = true;
            return;
        }

        combinedBounds.Encapsulate(transformedBounds.min);
        combinedBounds.Encapsulate(transformedBounds.max);
    }
}
