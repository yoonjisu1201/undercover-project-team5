using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    // 이 스포너가 속한 씬의 네트워크 씬 로드가 완료되면(=접속자 전원이 씬 로드를 마치면)
    // 서버만 스폰한다. GameSessionManager 등 다른 매니저에 의존하지 않고 스스로 트리거한다.
    private void OnEnable()
    {
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SceneManager == null) return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != gameObject.scene.name || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

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
            npc.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
        }
    }
}
