using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Box Collider로 나눈 구역의 통합 NavMesh 위에 등록된 단서를 서버 권한으로 생성하는 클래스.
// 단서는 ItemData/프리팹 하나를 전부 공유하고, 몇 번 단서인지는 스폰 시점에 번호를 배정해서 구분한다.
public sealed class ClueSpawner : MonoBehaviour, IRoundSpawner
{
    public static ClueSpawner Instance { get; private set; }

    [Header("단서 데이터")]
    [SerializeField] private ItemData _clueData;
    [SerializeField] private bool _spawnCluesAtRoundStart;
    [SerializeField, Min(0)] private int _totalClueCount = 8;
    [SerializeField, Min(0)] private int _missionRewardClueCount;

    public int SpawnCount => _spawnCluesAtRoundStart ? FieldClueCount : 0;
    private int FieldClueCount => Mathf.Max(0, _totalClueCount - _missionRewardClueCount);

    [Header("스폰 영역")]
    [SerializeField] private RoundSpawnCoordinator _spawnCoordinator;
    [SerializeField] private MapRegionController _basementRegionController;

    // 단서는 지하실에서만 획득한다. 지상 스폰 경로는 되살릴 수 있게 주석으로 남겨둔다.
    // [SerializeField] private MapRegionController _regionController;
    // [Tooltip("켜면 지상 구역 대신 Basement 전용 MapRegionController에서 스폰한다.")]
    // [SerializeField] private bool _useBasement;
    // private MapRegionController ActiveRegionController => _useBasement ? _basementRegionController : _regionController;

    private MapRegionController ActiveRegionController => _basementRegionController;

    [Header("배치 설정")]
    // 지상 스폰 규칙. 지상 경로를 되살릴 때 함께 살린다.
    // [SerializeField]
    // private SpawnRule _spawnRule = new()
    // {
    //     MinimumDistance = 5f,
    //     MaxAttempts = 50,
    //     HeightOffset = 0.04f,
    //     UseGroundPosition = true,
    //     ReservePosition = true
    // };

    // 지하는 방/복도가 좁아 지상과 같은 최소 거리를 쓰면 배치 실패가 잦아 별도로 둔다.
    [SerializeField]
    private SpawnRule _basementSpawnRule = new()
    {
        MinimumDistance = 10f,
        MaxAttempts = 50,
        HeightOffset = 0.04f,
        UseGroundPosition = true,
        ReservePosition = true
    };

    // 같은 최소 거리로 SpawnRule.MaxAttempts 만큼의 시도를 몇 번 더 반복할지.
    private const int SpawnRetryPasses = 50;

    private bool _hasSpawned;
    private readonly List<NetworkObject> _spawnedClues = new();

    // 이번 라운드에 배정된 단서 번호(필드 스폰 + 미션 보상 공통). 라운드가 바뀌면 초기화된다.
    private readonly HashSet<int> _usedClueNumbers = new();

    private CriminalNpcManager _criminalManager;

    // 스폰 시점엔 아직 범인이 확정되지 않아 _totalClueCount(8)로 우선 스폰한다.
    // 범인이 확정되면 실제 캡쳐 가능 개수(N)로 낮춰서 유령 번호가 나오지 않게 한다.
    private int _clueNumberCap;

    // 배치 규칙은 이 스포너가 코디네이터에 넘길 때만 쓴다.
    // private SpawnRule Rule => _useBasement ? _basementSpawnRule : _spawnRule;
    private SpawnRule Rule => _basementSpawnRule;

    private void Awake()
    {
        if (Instance == null) { Instance = this; }
        else { Destroy(gameObject); }

        _clueNumberCap = _totalClueCount;
    }

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
        if (_criminalManager != null)
        {
            _criminalManager.OnCriminalAssigned -= HandleCriminalAssigned;
        }

        if (NetworkManager.Singleton?.SceneManager == null)
        {
            return;
        }

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleSceneLoaded;
    }

    // ClueSpawner는 씬이 로딩 완료되면 단서를 스폰한다. 이 시점엔 범인이 아직 정해지지 않으므로
    // 범인이 확정되는 대로 개수를 다시 맞출 수 있게 CriminalNpcManager도 여기서 구독해둔다.
    private void HandleSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != gameObject.scene.name || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (_criminalManager == null)
        {
            _criminalManager = FindFirstObjectByType<CriminalNpcManager>();
            if (_criminalManager != null)
            {
                _criminalManager.OnCriminalAssigned += HandleCriminalAssigned;
            }
            else
            {
                Debug.LogError("[ClueSpawner] CriminalNpcManager를 찾지 못해 단서 개수를 범인 기준으로 맞출 수 없습니다.", this);
            }
        }

        if (!_hasSpawned)
        {
            SpawnClues();
        }
    }

    // 범인 외형이 확정될 때마다(라운드1 최초 지정 + 라운드2 이후 재지정) 호출되어, 이번 판 실제
    // 캡쳐 가능 개수(N)보다 큰 번호의 필드 단서를 제거하고 번호 풀 상한도 N으로 낮춘다.
    private void HandleCriminalAssigned()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        int capturableCount = ClueCaptureRules.CalculateCapturableClueCount(_criminalManager.CriminalFeature);
        _clueNumberCap = Mathf.Clamp(capturableCount, 0, _totalClueCount);

        for (int i = _spawnedClues.Count - 1; i >= 0; i--)
        {
            NetworkObject spawnedClue = _spawnedClues[i];
            if (spawnedClue == null || !spawnedClue.TryGetComponent(out ClueItem clueItem) || clueItem.ClueNumber <= _clueNumberCap)
            {
                continue;
            }

            _usedClueNumbers.Remove(clueItem.ClueNumber);
            _spawnedClues.RemoveAt(i);

            if (spawnedClue.IsSpawned)
            {
                spawnedClue.Despawn(destroy: true);
            }
        }

        Debug.Log($"[ClueSpawner] 범인 확정에 맞춰 이번 판 단서 개수를 {_clueNumberCap}개로 맞췄습니다.", this);
    }

    public void SpawnClues()
    {
        SpawnAsync(_spawnCoordinator, this.GetCancellationTokenOnDestroy()).Forget();
    }

    public UniTask SpawnAsync(RoundSpawnCoordinator coordinator, CancellationToken cancellationToken)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !ValidateSettings())
        {
            return UniTask.CompletedTask;
        }

        MapRegionController regionController = ActiveRegionController;

        if (!regionController.RefreshSpawnAreas())
        {
            Debug.LogError("[ClueSpawner] NavMesh가 포함된 단서 스폰 영역이 없습니다.", this);
            return UniTask.CompletedTask;
        }

        if (coordinator == null)
        {
            Debug.LogError("[ClueSpawner] RoundSpawnCoordinator가 설정되지 않았습니다.", this);
            return UniTask.CompletedTask;
        }

        _hasSpawned = true;

        for (int i = 0; i < FieldClueCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetClueSpawnPose(coordinator, regionController, out Vector3 spawnPosition, out Quaternion spawnRotation))
            {
                Debug.LogWarning("[ClueSpawner] 단서의 스폰 위치를 찾지 못했습니다.", this);
                continue;
            }

            if (!TryClaimRandomClueNumber(out int clueNumber))
            {
                Debug.LogWarning("[ClueSpawner] 배정 가능한 단서 번호가 없습니다.", this);
                break;
            }

            GameObject clueObject = Instantiate(
                _clueData.WorldPrefab,
                spawnPosition,
                spawnRotation);

            if (!clueObject.TryGetComponent(out ItemBase pickupItem) ||
                !clueObject.TryGetComponent(out NetworkObject networkObject))
            {
                Debug.LogError($"[ClueSpawner] '{_clueData.WorldPrefab.name}'에 ItemBase 또는 NetworkObject가 없습니다.", this);
                Destroy(clueObject);
                continue;
            }

            pickupItem.Configure(_clueData);
            networkObject.Spawn(destroyWithScene: true);
            (pickupItem as ClueItem)?.SetClueNumber(clueNumber);
            _spawnedClues.Add(networkObject);
        }

        return UniTask.CompletedTask;
    }

    // 지하 맵은 라운드마다 절차적으로 생성되어 좁게 나올 수 있고, 그러면 최소 거리를 만족하는 자리를 못 찾는다.
    // 단서는 _totalClueCount 개가 모두 있어야 하므로, 실패하면 최소 거리를 절반씩 줄여 가며 다시 시도한다.
    private bool TryGetClueSpawnPose(
        RoundSpawnCoordinator coordinator,
        MapRegionController regionController,
        out Vector3 spawnPosition,
        out Quaternion spawnRotation)
    {
        spawnPosition = default;
        spawnRotation = Quaternion.identity;

        SpawnRule rule = Rule;
        while (true)
        {
            // 후보 위치는 매번 무작위로 뽑히므로, 같은 최소 거리로 반복하는 것만으로 성공할 수 있다.
            for (int pass = 0; pass < SpawnRetryPasses; pass++)
            {
                if (coordinator.TryGetSpawnPose(regionController, rule, this, out _, out spawnPosition, out spawnRotation))
                {
                    return true;
                }
            }

            // 반복해도 안 되면 자리 자체가 부족한 것이므로 최소 거리를 낮춘다.
            if (rule.MinimumDistance <= 0f)
            {
                return false;
            }

            rule.MinimumDistance = rule.MinimumDistance < 1f ? 0f : rule.MinimumDistance * 0.5f;
            Debug.LogWarning($"[ClueSpawner] 스폰 자리를 찾지 못해 최소 거리를 {rule.MinimumDistance}로 줄여 다시 시도합니다.", this);
        }
    }

    // 아직 아무 데도 배정되지 않은 단서 번호 중 하나를 무작위로 배정한다.
    // 필드 스폰과 미션 보상(MissionInteractable) 양쪽에서 공통으로 써서 번호가 서로 겹치지 않게 한다.
    public bool TryClaimRandomClueNumber(out int clueNumber)
    {
        List<int> available = new();
        for (int number = 1; number <= _clueNumberCap; number++)
        {
            if (!_usedClueNumbers.Contains(number))
            {
                available.Add(number);
            }
        }

        if (available.Count == 0)
        {
            clueNumber = 0;
            return false;
        }

        clueNumber = available[Random.Range(0, available.Count)];
        _usedClueNumbers.Add(clueNumber);
        return true;
    }

    private bool ValidateSettings()
    {
        if (_clueData == null || _clueData.WorldPrefab == null)
        {
            Debug.LogError("[ClueSpawner] 단서 데이터를 설정해야 합니다.", this);
            return false;
        }

        if (ActiveRegionController == null)
        {
            Debug.LogError("[ClueSpawner] MapRegionController를 설정해야 합니다.", this);
            return false;
        }

        return true;
    }

    // 라운드 전환이 시작되면 이전 라운드의 필드 단서를 먼저 정리합니다.
    public void PrepareForNextRound()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[ClueSpawner] 서버에서만 단서를 정리할 수 있습니다.", this);
            return;
        }

        ClearSpawned();
    }

    // 다른 라운드 스포너의 재배치가 끝난 뒤 새 라운드 단서만 생성합니다.
    public void SpawnForNextRound()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[ClueSpawner] 서버에서만 단서를 생성할 수 있습니다.", this);
            return;
        }

        if (_spawnCluesAtRoundStart)
        {
            SpawnClues();
        }
        else
        {
            _hasSpawned = true;
        }
    }

    public void ClearSpawned()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        DespawnAllFieldClues();
        _spawnedClues.Clear();
        _usedClueNumbers.Clear();
        _clueNumberCap = _totalClueCount;
        _spawnCoordinator?.ClearPositions(this);
        _hasSpawned = false;
    }

    private void DespawnAllFieldClues()
    {
        // 최초 스폰 단서뿐만 아니라 플레이어가 다시 버린 단서까지 찾는다.
        ItemBase[] fieldItems = FindObjectsByType<ItemBase>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (ItemBase fieldItem in fieldItems)
        {
            if (fieldItem.ItemId != ItemType.Clue)
            {
                // 단서가 아닌 아이템은 무시
                continue;
            }

            // 누군가 인벤토리에 들고 있는 단서는 필드에 있는 게 아니므로 건드리지 않는다.
            if (fieldItem.IsStored)
            {
                continue;
            }

            NetworkObject networkObject = fieldItem.NetworkObject;

            if (networkObject != null && networkObject.IsSpawned)
            {
                networkObject.Despawn(destroy: true);
            }
        }
    }
}
