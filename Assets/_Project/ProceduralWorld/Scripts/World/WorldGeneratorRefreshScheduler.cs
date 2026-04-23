#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class WorldGeneratorRefreshScheduler
{
    private static readonly HashSet<int> PendingFull = new();
    private static readonly HashSet<int> PendingBiomeOnly = new();
    private static bool _flushQueued;

    static WorldGeneratorRefreshScheduler()
    {
        AssemblyReloadEvents.beforeAssemblyReload += Clear;
        EditorApplication.playModeStateChanged += _ => Clear();
    }

    public static void RequestAll(WorldGeneratorRefreshMode mode)
    {
        var generators = Object.FindObjectsByType<WorldGenerator>(FindObjectsSortMode.None);
        foreach (var generator in generators)
            Request(generator, mode);
    }

    public static void Request(WorldGenerator generator, WorldGeneratorRefreshMode mode)
    {
        if (generator == null)
            return;

        int id = generator.GetInstanceID();
        if (mode == WorldGeneratorRefreshMode.Full)
        {
            PendingFull.Add(id);
            PendingBiomeOnly.Remove(id);
        }
        else if (!PendingFull.Contains(id))
        {
            PendingBiomeOnly.Add(id);
        }

        QueueFlush();
    }

    private static void QueueFlush()
    {
        if (_flushQueued)
            return;

        _flushQueued = true;
        EditorApplication.delayCall += Flush;
    }

    private static void Flush()
    {
        _flushQueued = false;

        if (EditorApplication.isCompiling ||
            EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            QueueFlush();
            return;
        }

        var fullTargets = Resolve(PendingFull);
        var biomeTargets = Resolve(PendingBiomeOnly);
        Clear();

        foreach (var generator in fullTargets)
            generator.ProcessEditorRefresh(WorldGeneratorRefreshMode.Full);

        foreach (var generator in biomeTargets)
            generator.ProcessEditorRefresh(WorldGeneratorRefreshMode.BiomesOnly);
    }

    private static List<WorldGenerator> Resolve(HashSet<int> ids)
    {
        var resolved = new List<WorldGenerator>(ids.Count);
        var generators = Object.FindObjectsByType<WorldGenerator>(FindObjectsSortMode.None);
        foreach (var generator in generators)
        {
            if (generator != null && ids.Contains(generator.GetInstanceID()))
                resolved.Add(generator);
        }

        return resolved;
    }

    private static void Clear()
    {
        PendingFull.Clear();
        PendingBiomeOnly.Clear();
    }
}
#endif
