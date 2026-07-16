using UnityEngine;

/// <summary>
/// Checkpoint 위치에 NPC를 생성하고
/// 배회에 사용할 Checkpoint 목록을 전달합니다.
/// </summary>
public sealed class NpcSpawner : MonoBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField] private NpcStateMachine _npcPrefab;
    [SerializeField, Min(1)] private int _spawnCount = 150;

    [Header("Checkpoint Settings")]
    [SerializeField] private Transform[] _checkpoints;

    private void Start()
    {
        Spawn();
    }

    /// <summary>
    /// 설정된 수만큼 NPC를 생성합니다.
    /// </summary>
    public void Spawn()
    {
        if (_npcPrefab == null)
        {
            Debug.LogError("[NPC] 생성할 NPC Prefab이 없습니다.", this);
            return;
        }

        if (_checkpoints == null || _checkpoints.Length == 0)
        {
            Debug.LogError("[NPC] 사용할 Checkpoint가 없습니다.", this);
            return;
        }

        for (int index = 0; index < _spawnCount; index++)
        {
            int checkpointIndex = index % _checkpoints.Length;
            Transform spawnCheckpoint = _checkpoints[checkpointIndex];

            if (spawnCheckpoint == null)
            {
                Debug.LogError(
                    $"[NPC] Checkpoint 배열의 {checkpointIndex}번 항목이 비어 있습니다.",
                    this);
                return;
            }

            NpcStateMachine npc = Instantiate(
                _npcPrefab,
                spawnCheckpoint.position,
                spawnCheckpoint.rotation);

            npc.Configure(_checkpoints);
        }
    }
}
