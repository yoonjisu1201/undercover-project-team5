using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// 지하에 보스 한 마리를 서버 권한으로 생성한다.
//
// 다른 스포너들과 달리 개수가 1로 고정이다. 보스가 여러 마리면 어디서 오는지 읽을 수 없어져
// 숨는 판단 자체가 무의미해진다.
//
// 자리 선정을 RoundSpawnCoordinator 로 하지 않는다. 거기의 MinimumDistance 는 다른 스폰물
// (단서·건전지)의 예약 위치와 떨어진 거리라서 입구와는 아무 상관이 없고, 그대로 쓰면
// 보스가 입구 바로 앞에 놓일 수 있다. 대신 모듈 목록에서 입구로부터 먼 조각을 직접 고른다.
//
// 지하 맵은 라운드마다 절차적으로 다시 만들어지므로, 라운드가 바뀌면 새 맵 위에 다시 스폰해야 한다.
public sealed class BossSpawner : MonoBehaviour, IRoundSpawner
{
    [Header("보스")]
    [SerializeField] private GameObject _bossPrefab;

    [Tooltip("끄면 보스가 등장하지 않는다. 보스 없이 다른 부분을 테스트할 때 쓴다.")]
    [SerializeField] private bool _spawnBoss = true;

    [Header("입구에서 떨어뜨리기")]
    [Tooltip("입구(StartPoint)에서 문을 최소 몇 번 지나야 하는 조각에만 놓을지. 직선거리보다 이게 확실하다.")]
    [SerializeField, Min(1)] private int _minEntranceDepth = 3;

    [Tooltip("입구 조각 중심에서의 최소 직선거리(m). 맵이 접혀서 깊이는 멀지만 실제로 붙어 있는 경우를 막는다.")]
    [SerializeField, Min(0f)] private float _minEntranceDistance = 30f;

    [Tooltip("조각 중심에서 이 거리 안의 NavMesh를 찾는다. 중심이 벽 안일 수 있어 넉넉히 둔다.")]
    [SerializeField, Min(0.1f)] private float _navMeshSampleRadius = 6f;

    [Tooltip("조각 바닥에서 이만큼 띄운 곳을 기준으로 삼는다.")]
    [SerializeField, Min(0f)] private float _heightOffset = 0.5f;

    public int SpawnCount => _spawnBoss ? 1 : 0;

    private NetworkObject _spawnedBoss;
    private bool _hasSpawned;

    // 보스는 라운드마다 새로 스폰되므로 이전 외형을 기억하는 쪽은 씬에 남아 있는 이 스포너다.
    private int _lastModelIndex = -1;

    private readonly List<UndergroundModule> _candidates = new();

    private void OnEnable()
    {
        if (NetworkManager.Singleton?.SceneManager == null)
        {
            return;
        }

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton?.SceneManager == null)
        {
            return;
        }

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(
        string sceneName,
        LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut)
    {
        if (_hasSpawned ||
            sceneName != gameObject.scene.name ||
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        SpawnAsync(null, this.GetCancellationTokenOnDestroy()).Forget();
    }

    // coordinator 는 IRoundSpawner 규약을 맞추기 위한 것이고 이 스포너는 쓰지 않는다.
    public UniTask SpawnAsync(RoundSpawnCoordinator coordinator, CancellationToken cancellationToken)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return UniTask.CompletedTask;
        }

        if (!_spawnBoss)
        {
            // 스폰을 껐어도 처리한 것으로 표시해서 씬 로드 이벤트가 중복 전달될 때 매번 다시 들어오지 않게 한다.
            _hasSpawned = true;
            return UniTask.CompletedTask;
        }

        if (_bossPrefab == null)
        {
            Debug.LogError("[보스 스폰] 보스 프리팹을 설정해야 합니다.", this);
            return UniTask.CompletedTask;
        }

        if (!TryGetSpawnPosition(out Vector3 position))
        {
            Debug.LogWarning("[보스 스폰] 입구에서 충분히 떨어진 보스 자리를 찾지 못해 스폰하지 않았습니다.", this);
            return UniTask.CompletedTask;
        }

        _hasSpawned = true;

        GameObject boss = Instantiate(_bossPrefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        if (!boss.TryGetComponent(out NetworkObject networkObject))
        {
            Debug.LogError($"[보스 스폰] '{_bossPrefab.name}'에 NetworkObject가 없습니다.", this);
            Destroy(boss);
            return UniTask.CompletedTask;
        }

        networkObject.Spawn(destroyWithScene: true);
        _spawnedBoss = networkObject;

        // 외형은 스폰 뒤에 정해야 한다. NetworkVariable은 스폰 전에 쓸 수 없다.
        if (boss.TryGetComponent(out BossVisual visual))
        {
            visual.SetModelIndexOnServer(PickModelIndex(visual.ModelCount));
        }

        return UniTask.CompletedTask;
    }

    // 입구에서 문을 여러 번 지나야 닿는 조각들 중 하나를 무작위로 고른다.
    // 조건을 만족하는 조각이 없으면 가장 깊은 조각으로 물러난다 — 그게 이 맵에서 입구와 가장 먼 자리다.
    private bool TryGetSpawnPosition(out Vector3 position)
    {
        position = default;

        UndergroundRandomMapGenerator generator = FindFirstObjectByType<UndergroundRandomMapGenerator>();
        if (generator == null || generator.StartModule == null)
        {
            Debug.LogError("[보스 스폰] 지하 맵 생성기를 찾지 못했습니다.", this);
            return false;
        }

        Vector3 entrance = ModuleCenter(generator.StartModule);

        _candidates.Clear();
        UndergroundModule deepest = null;

        foreach (UndergroundModule module in generator.PlacedModules)
        {
            if (module == null || module.Bounds == null || module == generator.StartModule)
            {
                continue;
            }

            if (deepest == null || module.Depth > deepest.Depth)
            {
                deepest = module;
            }

            if (module.Depth < _minEntranceDepth)
            {
                continue;
            }

            if (Vector3.Distance(ModuleCenter(module), entrance) < _minEntranceDistance)
            {
                continue;
            }

            _candidates.Add(module);
        }

        if (_candidates.Count == 0)
        {
            if (deepest == null)
            {
                return false;
            }

            Debug.LogWarning(
                $"[보스 스폰] 깊이 {_minEntranceDepth} 이상, 입구에서 {_minEntranceDistance}m 이상인 조각이 없어 " +
                $"가장 깊은 조각(깊이 {deepest.Depth})에 놓습니다.", this);
            _candidates.Add(deepest);
        }

        // 후보를 섞어가며 NavMesh 위에 놓을 수 있는 첫 조각을 쓴다. 중심이 벽 안인 조각도 있다.
        for (int i = _candidates.Count - 1; i >= 0; i--)
        {
            int pick = Random.Range(0, i + 1);
            UndergroundModule module = _candidates[pick];
            _candidates[pick] = _candidates[i];

            if (NavMesh.SamplePosition(ModuleCenter(module), out NavMeshHit hit, _navMeshSampleRadius, NavMesh.AllAreas))
            {
                position = hit.position;
                return true;
            }
        }

        return false;
    }

    private Vector3 ModuleCenter(UndergroundModule module)
    {
        Bounds bounds = module.Bounds.bounds;
        return new Vector3(bounds.center.x, bounds.min.y + _heightOffset, bounds.center.z);
    }

    // 라운드마다 다른 외형이 나오게 한다. 무작위로만 뽑으면 같은 게 연달아 나와
    // "매번 다른 놈이 온다"는 느낌이 깨진다.
    private int PickModelIndex(int modelCount)
    {
        if (modelCount <= 0)
        {
            return -1;
        }

        if (modelCount == 1)
        {
            return 0;
        }

        int index = Random.Range(0, modelCount);
        if (index == _lastModelIndex)
        {
            index = (index + 1) % modelCount;
        }

        _lastModelIndex = index;
        return index;
    }

    // 라운드가 바뀌면 새 지하 맵 위에 다시 놓는다.
    // 이전 보스를 그대로 두면 사라진 모듈 안에 갇혀 NavMesh 밖에 남는다.
    public void RespawnBoss()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[보스 스폰] 서버에서만 보스를 재생성할 수 있습니다.", this);
            return;
        }

        ClearSpawned();
        SpawnAsync(null, this.GetCancellationTokenOnDestroy()).Forget();
    }

    public void ClearSpawned()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (_spawnedBoss != null && _spawnedBoss.IsSpawned)
        {
            _spawnedBoss.Despawn(destroy: true);
        }

        _spawnedBoss = null;
        _hasSpawned = false;
    }
}
