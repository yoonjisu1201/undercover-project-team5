using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// 해금된 MapRegion 안의 NavMesh에 NPC를 생성합니다.
public sealed class NpcSpawner : MonoBehaviour, IRoundSpawner
{
    [Header("Spawn Settings")]
    [SerializeField] private NpcStateMachine _npcPrefab;
    // 몇 마리 스폰할 때마다 한 프레임씩 양보할지 (로딩 패널이 화면에 그려질 틈을 주기 위함).
    [SerializeField, Min(1)] private int _spawnBatchSize = 10;

    [Header("Region Spawn Settings")]
    [SerializeField] private MapRegionController _regionController;
    [SerializeField] private RoundSpawnCoordinator _spawnCoordinator;
    [SerializeField]
    private SpawnRule _spawnRule = new()
    {
        MinimumDistance = 0f,
        MaxAttempts = 50,
        HeightOffset = 0f,
        UseGroundPosition = false,
        ReservePosition = false
    };

    // 스폰 목표 수. 현재 라운드 설정을 따른다 (RoundManager의 스폰 완료 확인용으로도 쓰인다).
    public int SpawnCount => RoundManager.Instance.NpcSpawnCount;

    // 배치 규칙은 이 스포너가 코디네이터에 넘길 때만 쓴다.
    private SpawnRule Rule => _spawnRule;

    private readonly List<NetworkObject> _spawnedNpcs = new();

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

        SpawnAsync(_spawnCoordinator, this.GetCancellationTokenOnDestroy()).Forget();
    }

    // 설정된 수만큼 NPC를 생성합니다.
    public async UniTask SpawnAsync(RoundSpawnCoordinator coordinator, CancellationToken cancellationToken)
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

        if (coordinator == null)
        {
            Debug.LogError("[NPC] RoundSpawnCoordinator가 설정되지 않았습니다.", this);
            return;
        }

        float spawnStart = Time.realtimeSinceStartup;
        Debug.Log($"[NPC] 스폰 시작: target={SpawnCount}, t={spawnStart:F2}s");

        for (int index = 0; index < SpawnCount; index++)
        {
            if (!coordinator.TryGetSpawnPose(_regionController, Rule, this, out MapRegion spawnRegion, out Vector3 spawnPosition, out Quaternion spawnRotation))
            {
                Debug.LogWarning($"[NPC] {index + 1}번째 NPC의 스폰 위치를 찾지 못했습니다. (t={Time.realtimeSinceStartup:F2}s)", this);
                continue;
            }

            NpcStateMachine npc = Instantiate(_npcPrefab, spawnPosition, spawnRotation);

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
            _spawnedNpcs.Add(networkObject);

            // 배치 단위로 한 프레임 양보해서, 로딩 패널이 화면에 그려질 틈을 준다.
            if ((index + 1) % _spawnBatchSize == 0)
            {
                Debug.Log($"[NPC] {index + 1}/{SpawnCount} 스폰됨, t={Time.realtimeSinceStartup:F2}s (누적 {Time.realtimeSinceStartup - spawnStart:F2}s)");
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        Debug.Log($"[NPC] 스폰 완료: t={Time.realtimeSinceStartup:F2}s (총 소요 {Time.realtimeSinceStartup - spawnStart:F2}s)");
    }

    // 기존 NPC를 제거한 뒤 현재 해방된 지역을 기준으로 다음 라운드 NPC를 다시 생성합니다.
    public async UniTask RespawnAsync(CancellationToken cancellationToken)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[NPC] 서버에서만 NPC를 재생성할 수 있습니다.", this);
            return;
        }

        ClearSpawned();
        await SpawnAsync(_spawnCoordinator, cancellationToken);
    }

    public void ClearSpawned()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        foreach (NetworkObject spawnedNpc in _spawnedNpcs)
        {
            if (spawnedNpc != null && spawnedNpc.IsSpawned)
            {
                spawnedNpc.Despawn(destroy: true);
            }
        }

        _spawnedNpcs.Clear();
        _spawnCoordinator?.ClearPositions(this);
    }
}
