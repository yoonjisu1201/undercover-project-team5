using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public interface IRoundSpawner
{
    int SpawnCount { get; }
    SpawnRule Rule { get; }

    UniTask SpawnAsync(RoundSpawnCoordinator coordinator, CancellationToken cancellationToken);

    void ClearSpawned();
}

[System.Serializable]
public struct SpawnRule
{
    [Min(0f)] public float MinimumDistance;
    [Min(1)] public int MaxAttempts;
    [Min(0f)] public float HeightOffset;

    public bool UseGroundPosition;
    public bool ReservePosition;
}
