using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// 네 종류의 건전지를 섞어 활성화된 맵 구역의 바닥에 네트워크 오브젝트로 생성한다.
public sealed class BatterySpawner : MonoBehaviour, IRoundSpawner
{
    // Inspector에 등록되어야 하는 20W, 30W, 40W, 50W 건전지 종류 수.
    private const int RequiredBatteryTypeCount = 4;

    [Header("건전지 데이터")]
    [SerializeField] private ItemData[] _batteryTypes;
    [SerializeField, Min(RequiredBatteryTypeCount)] private int _spawnCount = 20;

    [Header("스폰 영역")]
    [SerializeField] private MapRegionController _regionController;
    [SerializeField] private RoundSpawnCoordinator _spawnCoordinator;

    [Header("배치 설정")]
    [SerializeField]
    private SpawnRule _spawnRule = new()
    {
        MinimumDistance = 2f,
        MaxAttempts = 50,
        HeightOffset = 0.2f,
        UseGroundPosition = true,
        ReservePosition = true
    };

    // 이 스포너가 생성한 건전지를 보관해 라운드 정리 시 안전하게 Despawn한다.
    private readonly List<NetworkObject> _spawnedBatteries = new();
    // 같은 씬 로드 이벤트가 중복 전달되더라도 한 번만 생성하기 위한 상태값.
    private bool _hasSpawned;

    // IRoundSpawner가 외부에 제공하는 목표 생성 개수와 배치 규칙.
    public int SpawnCount => _spawnCount;
    public SpawnRule Rule => _spawnRule;

    // 네트워크 씬 로드가 모든 접속자에게 완료된 시점을 기다린다.
    private void OnEnable()
    {
        if (NetworkManager.Singleton?.SceneManager == null)
        {
            return;
        }

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleSceneLoaded;
    }

    // 오브젝트가 비활성화되거나 제거될 때 씬 이벤트 구독을 해제한다.
    private void OnDisable()
    {
        if (NetworkManager.Singleton?.SceneManager == null)
        {
            return;
        }

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        // 이 스포너가 속한 씬이 로드된 경우에만 서버 권한으로 한 번 생성한다.
        if (_hasSpawned || sceneName != gameObject.scene.name || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        SpawnAsync(_spawnCoordinator, this.GetCancellationTokenOnDestroy()).Forget();
    }

    // 설정된 수량의 건전지를 바닥에 배치하고 Netcode를 통해 모든 클라이언트에 생성한다.
    public UniTask SpawnAsync(RoundSpawnCoordinator coordinator, CancellationToken cancellationToken)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !ValidateSettings())
        {
            return UniTask.CompletedTask;
        }

        if (!_regionController.RefreshSpawnAreas())
        {
            Debug.LogError("[BatterySpawner] 건전지를 생성할 맵 구역이 없습니다.", this);
            return UniTask.CompletedTask;
        }

        if (coordinator == null)
        {
            Debug.LogError("[BatterySpawner] RoundSpawnCoordinator가 설정되지 않았습니다.", this);
            return UniTask.CompletedTask;
        }

        _hasSpawned = true;

        // 네 종류가 최소 하나씩 포함된 무작위 생성 순서를 먼저 확정한다.
        List<ItemData> spawnQueue = CreateSpawnQueue();

        // 생성할 건전지의 개수만큼 반복하며 스폰 위치를 찾고, 건전지 프리팹을 생성한다.
        for (int index = 0; index < spawnQueue.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ItemData batteryData = spawnQueue[index];

            // 활성 구역의 바닥에서 다른 예약 위치와 충분히 떨어진 지점을 찾는다.
            if (!coordinator.TryGetSpawnPose(_regionController, Rule, this, out _, out Vector3 spawnPosition, out Quaternion spawnRotation))
            {
                Debug.LogWarning($"[BatterySpawner] {index + 1}번째 건전지의 스폰 위치를 찾지 못했습니다.", this);
                continue;
            }

            // 건전지가 세워지지 않고 바닥에 옆으로 눕도록 고정 회전을 적용한다.
            spawnRotation = Quaternion.Euler(90f, 0f, 90f);
            GameObject batteryObject = Instantiate(batteryData.WorldPrefab, spawnPosition, spawnRotation);

            if (!batteryObject.TryGetComponent(out ItemBase pickupItem) ||
                !batteryObject.TryGetComponent(out NetworkObject networkObject))
            {
                Debug.LogError($"[BatterySpawner] '{batteryData.WorldPrefab.name}'에 ItemBase 또는 NetworkObject가 없습니다.", this);
                Destroy(batteryObject);
                continue;
            }

            // 프리팹의 공용 PickupItem에 선택된 종류를 전달한 뒤 네트워크에 스폰한다.
            pickupItem.Configure(batteryData);
            networkObject.Spawn(destroyWithScene: true);
            _spawnedBatteries.Add(networkObject);
        }

        return UniTask.CompletedTask;
    }

    // 네 종류를 최소 하나씩 포함하고, 남은 개수는 무작위 종류로 채운 뒤 순서를 섞는다.
    private List<ItemData> CreateSpawnQueue()
    {
        List<ItemData> spawnQueue = new(_spawnCount);
        // 먼저 네 종류를 한 개씩 넣어 특정 종류가 한 번도 나오지 않는 상황을 막는다.
        spawnQueue.AddRange(_batteryTypes);

        // 나머지 수량은 네 종류 중 무작위로 선택한다.
        while (spawnQueue.Count < _spawnCount)
        {
            spawnQueue.Add(_batteryTypes[Random.Range(0, _batteryTypes.Length)]);
        }

        // Fisher-Yates 방식으로 목록을 섞어 종류별 생성 순서가 고정되지 않게 한다.
        for (int index = spawnQueue.Count - 1; index > 0; index--)
        {
            int randomIndex = Random.Range(0, index + 1);
            (spawnQueue[index], spawnQueue[randomIndex]) = (spawnQueue[randomIndex], spawnQueue[index]);
        }

        return spawnQueue;
    }

    // 기존 건전지를 정리하고 현재 해방된 지역을 기준으로 다시 생성합니다.
    public void RespawnBatteries()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[BatterySpawner] 서버에서만 건전지를 재생성할 수 있습니다.", this);
            return;
        }

        ClearSpawned();
        SpawnAsync(_spawnCoordinator, this.GetCancellationTokenOnDestroy()).Forget();
    }

    // 이 스포너가 생성한 건전지와 예약 위치를 정리해 다음 라운드에 다시 생성할 수 있게 한다.
    public void ClearSpawned()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        foreach (NetworkObject spawnedBattery in _spawnedBatteries)
        {
            if (spawnedBattery != null && spawnedBattery.IsSpawned)
            {
                spawnedBattery.Despawn(destroy: true);
            }
        }

        _spawnedBatteries.Clear();
        _spawnCoordinator?.ClearPositions(this);
        _hasSpawned = false;
    }

    // 스폰 전에 네 종류의 데이터, 프리팹, 맵 참조가 모두 준비됐는지 검사한다.
    private bool ValidateSettings()
    {
        if (_batteryTypes == null || _batteryTypes.Length != RequiredBatteryTypeCount)
        {
            Debug.LogError($"[BatterySpawner] 건전지 데이터는 정확히 {RequiredBatteryTypeCount}종이 필요합니다.", this);
            return false;
        }

        foreach (ItemData batteryData in _batteryTypes)
        {
            if (batteryData == null || batteryData.WorldPrefab == null)
            {
                Debug.LogError($"[BatterySpawner] 비어 있거나 WorldPrefab이 없는 건전지 데이터가 있습니다.", this);
                return false;
            }
        }

        if (_regionController == null)
        {
            Debug.LogError($"[BatterySpawner] MapRegionController를 설정해야 합니다.", this);
            return false;
        }

        return true;
    }
}
