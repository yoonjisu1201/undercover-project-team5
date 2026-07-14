using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed class NpcCheckpointDestinationProvider : INpcDestinationProvider
{
    private readonly MapBlockController _blockController;
    private readonly float _sampleDistance;
    private readonly int _maxAttempts;
    private readonly NavMeshPath _path = new();

    public NpcCheckpointDestinationProvider(
        MapBlockController blockController,
        float sampleDistance,
        int maxAttempts)
    {
        _blockController = blockController;
        _sampleDistance = Mathf.Max(0f, sampleDistance);
        _maxAttempts = Mathf.Max(1, maxAttempts);
    }

    /// <summary>
    /// 해제된 Block의 Checkpoint를 무작위 순서로 확인하고, 점유와
    /// PathComplete 검증에 성공한 목적지를 예약합니다. 다른 Checkpoint를
    /// 우선하며 실패하면 현재 Checkpoint 반경을 제한된 횟수만큼 재시도합니다.
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

        List<NpcCheckpoint> candidates = CollectOtherCandidates(currentCheckpoint);
        Shuffle(candidates);

        int otherAttemptCount = Mathf.Min(_maxAttempts, candidates.Count);

        for (int attempt = 0; attempt < otherAttemptCount; attempt++)
        {
            NpcCheckpoint checkpoint = candidates[attempt];

            if (!checkpoint.TryOccupy())
            {
                continue;
            }

            if (TryFindReachablePosition(origin, checkpoint, out Vector3 position))
            {
                destination = new NpcDestination(position, checkpoint);
                return true;
            }

            checkpoint.Release();
        }

        if (currentCheckpoint == null)
        {
            return false;
        }

        for (int attempt = 0; attempt < _maxAttempts; attempt++)
        {
            if (TryFindReachablePosition(
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

        destination.Checkpoint.Release();
    }

    private List<NpcCheckpoint> CollectOtherCandidates(NpcCheckpoint currentCheckpoint)
    {
        List<NpcCheckpoint> candidates = new();
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
                    !checkpoint.HasVacancy ||
                    candidates.Contains(checkpoint))
                {
                    continue;
                }

                candidates.Add(checkpoint);
            }
        }

        return candidates;
    }

    private bool TryFindReachablePosition(
        Vector3 origin,
        NpcCheckpoint checkpoint,
        out Vector3 position)
    {
        position = default;
        Vector3 randomPoint = checkpoint.RandomPointInRadius();

        if (!NavMesh.SamplePosition(
                randomPoint,
                out NavMeshHit hit,
                _sampleDistance,
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
            (checkpoints[index], checkpoints[swapIndex]) =
                (checkpoints[swapIndex], checkpoints[index]);
        }
    }
}
