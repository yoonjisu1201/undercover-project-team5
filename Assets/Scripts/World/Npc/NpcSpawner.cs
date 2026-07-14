using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed class NpcSpawner : MonoBehaviour
{
    [SerializeField] private NpcPool _pool;
    [SerializeField] private MapBlockController _blockController;
    [SerializeField, Min(0)] private int _targetCount = 50;
    [SerializeField, Min(0f)] private float _minimumSpawnSpacing = 1f;
    [SerializeField, Min(1)] private int _maxSpawnAttempts = 20;
    [SerializeField, Min(0f)] private float _navMeshSampleDistance = 2f;

    private readonly List<NpcController> _activeNpcs = new();
    private bool _hasSpawnedRound;

    /// <summary>
    /// 라운드 시작 시 한 번만 실행되어 해제된 Block의 Checkpoint 안에
    /// 목표 수만큼 NPC를 배치합니다. Checkpoint 예약, NavMesh 좌표,
    /// NPC 간 최소 간격과 고정 Pool 용량을 만족하지 못한 수는 한 번만 기록합니다.
    /// </summary>
    public void SpawnRound()
    {
        if (_hasSpawnedRound)
        {
            return;
        }

        _hasSpawnedRound = true;

        int requestedCount = Mathf.Max(0, _targetCount);

        if (_pool == null)
        {
            LogMissing(requestedCount, requestedCount);
            return;
        }

        for (int spawnIndex = 0; spawnIndex < requestedCount; spawnIndex++)
        {
            if (!TryReserveSpawnPosition(
                    out NpcCheckpoint checkpoint,
                    out Vector3 position))
            {
                continue;
            }

            if (!_pool.TryRentNpc(out NpcController npc))
            {
                checkpoint.ReleaseReservation();
                break;
            }

            npc.AssignSpawnCheckpoint(checkpoint, position);
            _pool.Activate(npc, position);
            _activeNpcs.Add(npc);
        }

        int missingCount = requestedCount - _activeNpcs.Count;
        LogMissing(requestedCount, missingCount);
    }

    /// <summary>
    /// 현재 라운드에서 활성화한 NPC를 Pool에 반환하고 다음 라운드 배치를 허용합니다.
    /// </summary>
    public void ReturnRound()
    {
        if (_pool != null)
        {
            for (int index = _activeNpcs.Count - 1; index >= 0; index--)
            {
                _pool.Return(_activeNpcs[index]);
            }
        }

        _activeNpcs.Clear();
        _hasSpawnedRound = false;
    }

    private bool TryReserveSpawnPosition(
        out NpcCheckpoint checkpoint,
        out Vector3 position)
    {
        checkpoint = null;
        position = default;

        List<NpcCheckpoint> candidates = CollectVacantCheckpoints();

        for (int attempt = 0;
            attempt < Mathf.Max(1, _maxSpawnAttempts) && candidates.Count > 0;
            attempt++)
        {
            int candidateIndex = Random.Range(0, candidates.Count);
            NpcCheckpoint candidate = candidates[candidateIndex];

            if (!candidate.TryReserve())
            {
                candidates.RemoveAt(candidateIndex);
                continue;
            }

            if (TrySampleSpawnPosition(candidate, out Vector3 sampledPosition))
            {
                checkpoint = candidate;
                position = sampledPosition;
                return true;
            }

            candidate.ReleaseReservation();
        }

        return false;
    }

    private List<NpcCheckpoint> CollectVacantCheckpoints()
    {
        List<NpcCheckpoint> checkpoints = new();

        if (_blockController == null)
        {
            return checkpoints;
        }

        IReadOnlyList<MapBlock> availableBlocks = _blockController.AvailableBlocks;

        for (int blockIndex = 0; blockIndex < availableBlocks.Count; blockIndex++)
        {
            MapBlock block = availableBlocks[blockIndex];

            if (block == null || block.Checkpoints == null)
            {
                continue;
            }

            IReadOnlyList<NpcCheckpoint> blockCheckpoints = block.Checkpoints;

            for (int checkpointIndex = 0;
                checkpointIndex < blockCheckpoints.Count;
                checkpointIndex++)
            {
                NpcCheckpoint checkpoint = blockCheckpoints[checkpointIndex];

                if (checkpoint != null &&
                    checkpoint.HasAvailableSlot &&
                    !checkpoints.Contains(checkpoint))
                {
                    checkpoints.Add(checkpoint);
                }
            }
        }

        return checkpoints;
    }

    private bool TrySampleSpawnPosition(
        NpcCheckpoint checkpoint,
        out Vector3 position)
    {
        position = default;
        Vector3 randomPoint = checkpoint.RandomPointInRadius();

        if (!NavMesh.SamplePosition(
                randomPoint,
                out NavMeshHit hit,
                Mathf.Max(0f, _navMeshSampleDistance),
                NavMesh.AllAreas))
        {
            return false;
        }

        if (!checkpoint.Contains(hit.position) ||
            !HasMinimumSpawnSpacing(hit.position))
        {
            return false;
        }

        position = hit.position;
        return true;
    }

    private bool HasMinimumSpawnSpacing(Vector3 position)
    {
        float minimumSpacing = Mathf.Max(0f, _minimumSpawnSpacing);
        float minimumSpacingSquared = minimumSpacing * minimumSpacing;

        for (int index = 0; index < _activeNpcs.Count; index++)
        {
            NpcController npc = _activeNpcs[index];

            if (npc != null &&
                (npc.transform.position - position).sqrMagnitude < minimumSpacingSquared)
            {
                return false;
            }
        }

        return true;
    }

    private void LogMissing(int requestedCount, int missingCount)
    {
        if (missingCount <= 0)
        {
            return;
        }

        int spawnedCount = requestedCount - missingCount;
        Debug.Log(
            $"[NPC] Round spawn requested {requestedCount}, " +
            $"spawned {spawnedCount}, missing {missingCount}.",
            this);
    }
}
