using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed class NpcCheckpointDestinationProvider : INpcDestinationProvider
{
    private readonly MapBlockController _blockController;
    private readonly float _searchRadius;
    private readonly int _maxAttempts;
    private readonly NavMeshPath _path = new();

    public NpcCheckpointDestinationProvider(
        MapBlockController blockController,
        float searchRadius,
        int maxAttempts)
    {
        _blockController = blockController;
        _searchRadius = Mathf.Max(0f, searchRadius);
        _maxAttempts = Mathf.Max(1, maxAttempts);
    }

    /// <summary>
    /// 해제된 Block의 Checkpoint를 무작위 순서로 확인하고, 예약과
    /// PathComplete 검증에 성공한 목적지를 반환합니다. 현재 Checkpoint가 아닌
    /// 후보를 우선하며, 다른 후보가 실패하면 현재 Checkpoint 주변을 제한적으로 재시도합니다.
    /// </summary>
    /// <param name="origin">경로를 시작할 NPC의 현재 위치입니다.</param>
    /// <param name="currentCheckpoint">NPC가 현재 예약한 Checkpoint입니다.</param>
    /// <param name="destination">예약에 성공한 목적지와 Checkpoint입니다.</param>
    /// <returns>유효한 목적지를 예약하면 true입니다.</returns>
    public bool TryReserveDestination(
        Vector3 origin,
        NpcCheckpoint currentCheckpoint,
        out NpcDestination destination)
    {
        destination = null;

        if (_blockController == null)
        {
            return false;
        }

        List<NpcCheckpoint> candidates = CollectAvailableCandidates(currentCheckpoint);
        Shuffle(candidates);

        int candidateAttemptCount = Mathf.Min(_maxAttempts, candidates.Count);

        for (int attempt = 0; attempt < candidateAttemptCount; attempt++)
        {
            NpcCheckpoint checkpoint = candidates[attempt];

            if (!checkpoint.TryReserve())
            {
                continue;
            }

            if (TryFindValidPositionInCheckpoint(origin, checkpoint, out Vector3 position))
            {
                destination = new NpcDestination(position, checkpoint);
                return true;
            }

            checkpoint.ReleaseReservation();
        }

        if (currentCheckpoint == null)
        {
            return false;
        }

        for (int attempt = 0; attempt < _maxAttempts; attempt++)
        {
            if (TryFindValidPositionInCheckpoint(
                    origin,
                    currentCheckpoint,
                    out Vector3 fallbackPosition))
            {
                destination = new NpcDestination(fallbackPosition, currentCheckpoint);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 목적지가 보유한 Checkpoint 예약을 한 번 해제합니다.
    /// </summary>
    /// <param name="destination">해제할 예약 정보입니다.</param>
    public void ReleaseDestination(NpcDestination destination)
    {
        if (destination == null || destination.Checkpoint == null)
        {
            return;
        }

        destination.Checkpoint.ReleaseReservation();
    }

    private List<NpcCheckpoint> CollectAvailableCandidates(NpcCheckpoint currentCheckpoint)
    {
        List<NpcCheckpoint> candidates = new List<NpcCheckpoint>();
        IReadOnlyList<MapBlock> availableBlocks = _blockController.AvailableBlocks;

        for (int blockIndex = 0; blockIndex < availableBlocks.Count; blockIndex++)
        {
            MapBlock block = availableBlocks[blockIndex];

            if (block == null || block.Checkpoints == null)
            {
                continue;
            }

            IReadOnlyList<NpcCheckpoint> checkpoints = block.Checkpoints;

            for (int checkpointIndex = 0; checkpointIndex < checkpoints.Count; checkpointIndex++)
            {
                NpcCheckpoint checkpoint = checkpoints[checkpointIndex];

                if (checkpoint == null ||
                    checkpoint == currentCheckpoint ||
                    !checkpoint.HasAvailableSlot ||
                    candidates.Contains(checkpoint))
                {
                    continue;
                }

                candidates.Add(checkpoint);
            }
        }

        return candidates;
    }

    private bool TryFindValidPositionInCheckpoint(
        Vector3 origin,
        NpcCheckpoint checkpoint,
        out Vector3 position)
    {
        position = default;
        Vector3 randomPoint = checkpoint.RandomPointInRadius();

        if (!NavMesh.SamplePosition(
                randomPoint,
                out NavMeshHit hit,
                _searchRadius,
                NavMesh.AllAreas))
        {
            return false;
        }

        if (!checkpoint.Contains(hit.position))
        {
            return false;
        }

        bool hasPath = NavMesh.CalculatePath(
            origin,
            hit.position,
            NavMesh.AllAreas,
            _path);

        if (!hasPath || _path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        position = hit.position;

        return true;
    }

    private static void Shuffle(List<NpcCheckpoint> checkpoints)
    {
        for (int index = checkpoints.Count - 1; index > 0; index--)
        {
            int swapIndex = Random.Range(0, index + 1);

            NpcCheckpoint temporaryCheckpoint = checkpoints[index];

            checkpoints[index] = checkpoints[swapIndex];
            checkpoints[swapIndex] = temporaryCheckpoint;
        }
    }
}
