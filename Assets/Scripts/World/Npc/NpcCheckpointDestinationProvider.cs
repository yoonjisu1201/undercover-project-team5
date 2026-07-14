using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 해금된 Block의 Checkpoint를 탐색해 예약·NavMesh 위치·완전 경로를 검증합니다.
/// </summary>
public sealed class NpcCheckpointDestinationProvider : INpcDestinationProvider
{
    private readonly MapBlockController _blockController;
    private readonly float _searchRadius;
    private readonly int _maxAttempts;
    private readonly NavMeshPath _path = new();

    /// <summary>
    /// 후보를 조회할 Controller와 NavMesh 샘플·시도 한도를 저장합니다.
    /// </summary>
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
    /// 열린 Checkpoint에서 예약·반경·완전 경로를 모두 만족하는 목적지를 확보합니다.
    /// 다른 Checkpoint를 우선하고, 실패 시 현재 Checkpoint를 재예약 없이 시도합니다.
    /// </summary>
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
    /// 목적지가 보유한 Checkpoint 예약을 한 번 반납합니다.
    /// </summary>
    public void ReleaseDestination(NpcDestination destination)
    {
        if (destination == null || destination.Checkpoint == null)
        {
            return;
        }

        destination.Checkpoint.ReleaseReservation();
    }

    /// <summary>
    /// 현재 Checkpoint를 제외하고 해금된 Block의 빈 Checkpoint를 수집합니다.
    /// </summary>
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

    /// <summary>
    /// 반경 샘플, NavMesh 포함 여부, PathComplete를 순서대로 검증합니다.
    /// </summary>
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

    /// <summary>
    /// 후보 순서를 섞어 특정 Checkpoint 편중을 줄입니다.
    /// </summary>
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
