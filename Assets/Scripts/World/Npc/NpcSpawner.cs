using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// 해금된 MapRegion 안의 NavMesh에 NPC를 생성합니다.
public sealed class NpcSpawner : MonoBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField] private NpcStateMachine _npcPrefab;
    [SerializeField, Min(1)] private int _spawnCount = 150;

    [Header("Region Spawn Settings")]
    [SerializeField] private MapRegionController _regionController;

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

    // 설정된 수만큼 NPC를 생성합니다.
    public void Spawn()
    {
        if (_npcPrefab == null)
        {
            Debug.LogError("[NPC] 생성할 NPC Prefab이 없습니다.", this);
            return;
        }

        if (_regionController == null || !_regionController.RefreshSpawnAreas())
        {
            Debug.LogError("[NPC] 해금된 MapRegion 안에 NPC를 생성할 NavMesh 영역이 없습니다.", this);
            return;
        }

        for (int index = 0; index < _spawnCount; index++)
        {
            if (!_regionController.TryGetRandomSpawnPoint(out MapRegion spawnRegion, out Vector3 spawnPosition))
            {
                Debug.LogWarning($"[NPC] {index + 1}번째 NPC의 스폰 위치를 찾지 못했습니다.", this);
                continue;
            }

            NpcStateMachine npc = Instantiate(
                _npcPrefab,
                spawnPosition,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            if (npc.TryGetComponent(out NpcRandomWander randomWander))
            {
                randomWander.Initialize(spawnRegion);
            }

            if (!npc.TryGetComponent(out NetworkObject networkObject))
            {
                Debug.LogError($"[NPC] '{_npcPrefab.name}'에 NetworkObject가 없습니다.", this);
                Destroy(npc.gameObject);
                return;
            }

            networkObject.Spawn(destroyWithScene: true);
        }
    }

}
