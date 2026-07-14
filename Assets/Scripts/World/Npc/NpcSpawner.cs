using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 해금된 Block의 Checkpoint에 NPC를 한 라운드 배치하고 반환합니다.
/// 예약·NavMesh·최소 간격을 확인하며 부족분은 라운드 단위로 집계합니다.
/// </summary>
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
    /// 라운드당 한 번, 수용량·NavMesh·간격을 만족하는 위치에 Pool NPC를 배치합니다.
    /// 실패 단계에서는 예약을 되돌리며 Pool을 실행 중 증설하지 않습니다.
    /// </summary>
    public void SpawnRound()
    {
        if (_hasSpawnedRound)
        {
            return;
        }

        _hasSpawnedRound = true;

        int requestedCount = Mathf.Max(0, _targetCount);

        if (_blockController == null)
        {
            Debug.LogError(
                "[NPC] MapBlockController가 없어 라운드 배치를 시작할 수 없습니다.",
                this);
            return;
        }

        if (_pool == null)
        {
            Debug.LogError(
                "[NPC] NpcPool이 없어 라운드 배치를 시작할 수 없습니다.",
                this);
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

            npc.Configure(_blockController);
            npc.AssignSpawnCheckpoint(checkpoint, position);
            _pool.Activate(npc, position);
            _activeNpcs.Add(npc);
        }

        // 개별 실패 대신 라운드 종료 시 부족 인원만 한 번 집계합니다.
        int missingCount = requestedCount - _activeNpcs.Count;
        LogMissing(requestedCount, missingCount);
    }

    /// <summary>
    /// 이번 라운드에서 활성화한 NPC만 Pool로 반환하고 다음 배치를 허용합니다.
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

    /// <summary>
    /// 후보 Checkpoint에서 예약과 스폰 위치 검증을 통과한 한 곳을 확보합니다.
    /// 실패한 예약은 즉시 반환합니다.
    /// </summary>
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

    /// <summary>
    /// 해금된 Block에서 아직 수용량이 남은 중복 없는 Checkpoint를 수집합니다.
    /// </summary>
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

    /// <summary>
    /// Checkpoint 반경, NavMesh 포함 여부, 기존 NPC와의 간격을 순서대로 검증합니다.
    /// </summary>
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

    /// <summary>
    /// 후보 위치가 기존 활성 NPC와 최소 간격을 유지하는지 확인합니다.
    /// </summary>
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

    /// <summary>
    /// 개별 실패를 반복 출력하지 않고 라운드 전체 부족분만 기록합니다.
    /// </summary>
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
