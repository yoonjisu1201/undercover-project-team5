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
        if (!HasValidConfiguration())
        {
            return;
        }

        for (int index = 0; index < _spawnCount; index++)
        {
            Transform spawnCheckpoint =
                _checkpoints[index % _checkpoints.Length];

            NpcStateMachine npc = Instantiate(
                _npcPrefab,
                spawnCheckpoint.position,
                spawnCheckpoint.rotation);

            npc.Configure(_checkpoints);
        }
    }

    /// <summary>
    /// NPC 생성에 필요한 Inspector 설정을 확인합니다.
    /// </summary>
    private bool HasValidConfiguration()
    {
        if (_npcPrefab == null)
        {
            Debug.LogError(
                "[NPC] 생성할 NPC Prefab이 없습니다.",
                this);

            return false;
        }

        if (_spawnCount <= 0)
        {
            Debug.LogError(
                "[NPC] Spawn Count는 1 이상이어야 합니다.",
                this);

            return false;
        }

        if (_checkpoints == null || _checkpoints.Length == 0)
        {
            Debug.LogError(
                "[NPC] 사용할 Checkpoint가 없습니다.",
                this);

            return false;
        }

        for (int index = 0; index < _checkpoints.Length; index++)
        {
            if (_checkpoints[index] != null)
            {
                continue;
            }

            Debug.LogError(
                $"[NPC] Checkpoint 배열의 {index}번 항목이 비어 있습니다.",
                this);

            return false;
        }

        return true;
    }
}