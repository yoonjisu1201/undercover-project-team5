using System.Collections.Generic;
using UnityEngine;


public sealed class RoundSpawnCoordinator : MonoBehaviour
{
    private readonly List<ReservedPosition> _reservedPositions = new();

    public bool TryGetSpawnPose(MapRegionController regionController, SpawnRule rule, object owner, out MapRegion region, out Vector3 position, out Quaternion rotation)
    {
        int maxAttempts = Mathf.Max(1, rule.MaxAttempts);

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (regionController == null || !regionController.TryGetRandomSpawnPoint(out region, out Vector3 candidate))
            {
                continue;
            }

            if (rule.UseGroundPosition && !region.SpawnArea.TryGetGroundPoint(candidate, out candidate))
            {
                continue;
            }

            candidate += Vector3.up * rule.HeightOffset;

            if (!IsFarEnough(candidate, rule.MinimumDistance))
            {
                continue;
            }

            position = candidate;
            rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            if (rule.ReservePosition)
            {
                _reservedPositions.Add(new ReservedPosition(owner, position));
            }

            return true;
        }

        region = null;
        position = default;
        rotation = Quaternion.identity;
        return false;
    }

    private bool IsFarEnough(Vector3 candidate, float minimumDistance)
    {
        if (minimumDistance <= 0f)
        {
            return true;
        }

        float minimumDistanceSquared = minimumDistance * minimumDistance;

        foreach (ReservedPosition reservedPosition in _reservedPositions)
        {
            if ((reservedPosition.Position - candidate).sqrMagnitude < minimumDistanceSquared)
            {
                return false;
            }
        }

        return true;
    }

    public void ClearPositions()
    {
        _reservedPositions.Clear();
    }

    public void ClearPositions(object owner)
    {
        _reservedPositions.RemoveAll(reservedPosition => ReferenceEquals(reservedPosition.Owner, owner));
    }

    private readonly struct ReservedPosition
    {
        public ReservedPosition(object owner, Vector3 position)
        {
            Owner = owner;
            Position = position;
        }

        public object Owner { get; }
        public Vector3 Position { get; }
    }
}
