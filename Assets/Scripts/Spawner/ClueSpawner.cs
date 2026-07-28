using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Box Collider로 나눈 구역의 통합 NavMesh 위에 등록된 단서를 서버 권한으로 생성하는 클래스
public sealed class ClueSpawner : MonoBehaviour, IRoundSpawner
{
    [Header("단서 데이터")]
    [SerializeField] private ItemData[] _clues;

    public int SpawnCount => _clues?.Length ?? 0;

    [Header("스폰 영역")]
    [SerializeField] private MapRegionController _regionController;
    [SerializeField] private RoundSpawnCoordinator _spawnCoordinator;

    [Header("배치 설정")]
    [SerializeField]
    private SpawnRule _spawnRule = new()
    {
        MinimumDistance = 5f,
        MaxAttempts = 50,
        HeightOffset = 0.04f,
        UseGroundPosition = true,
        ReservePosition = true
    };

    private bool _hasSpawned;
    private readonly List<NetworkObject> _spawnedClues = new();
    private const string ClueItemIdPrefix = "Clue";

    public SpawnRule Rule => _spawnRule;

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

    private void HandleSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (_hasSpawned || sceneName != gameObject.scene.name || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        // 플레이어 인벤토리를 초기화하고 단서를 새로 스폰
        RoundManager.Instance?.ClearAllPlayerInventories();

        SpawnAsync(_spawnCoordinator, this.GetCancellationTokenOnDestroy()).Forget();
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

        if (!_regionController.RefreshSpawnAreas())
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

        foreach (ItemData clueData in _clues)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!coordinator.TryGetSpawnPose(
                    _regionController,
                    Rule,
                    this,
                    out _,
                    out Vector3 spawnPosition,
                    out Quaternion spawnRotation))
            {
                Debug.LogWarning($"[ClueSpawner] '{clueData.ItemId}'의 스폰 위치를 찾지 못했습니다.", this);
                continue;
            }

            GameObject clueObject = Instantiate(
                clueData.WorldPrefab,
                spawnPosition,
                spawnRotation);

            if (!clueObject.TryGetComponent(out PickupItem pickupItem) ||
                !clueObject.TryGetComponent(out NetworkObject networkObject))
            {
                Debug.LogError($"[ClueSpawner] '{clueData.WorldPrefab.name}'에 PickupItem 또는 NetworkObject가 없습니다.", this);
                Destroy(clueObject);
                continue;
            }

            pickupItem.Configure(clueData);
            networkObject.Spawn(destroyWithScene: true);
            _spawnedClues.Add(networkObject);
        }

        return UniTask.CompletedTask;
    }

    private bool ValidateSettings()
    {
        if (_clues == null || _clues.Length == 0)
        {
            Debug.LogError("[ClueSpawner] 단서 데이터를 하나 이상 등록해야 합니다.", this);
            return false;
        }

        foreach (ItemData clueData in _clues)
        {
            if (clueData == null || clueData.WorldPrefab == null)
            {
                Debug.LogError("[ClueSpawner] 비어 있거나 WorldPrefab이 없는 단서 데이터가 있습니다.", this);
                return false;
            }
        }

        if (_regionController == null)
        {
            Debug.LogError("[ClueSpawner] MapRegionController를 설정해야 합니다.", this);
            return false;
        }

        return true;
    }

    public void RespawnClues()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[ClueSpawner] 서버에서만 단서를 재생성할 수 있습니다.", this);
            return;
        }

        RoundManager.Instance?.ClearAllPlayerInventories();
        ClearSpawned();

        SpawnClues();
    }

    public void ClearSpawned()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        DespawnAllFieldClues();
        _spawnedClues.Clear();
        _spawnCoordinator?.ClearPositions(this);
        _hasSpawned = false;
    }

    private void DespawnAllFieldClues()
    {
        // 최초 스폰 단서뿐만 아니라 플레이어가 다시 버린 단서까지 찾는다.
        PickupItem[] fieldItems = FindObjectsByType<PickupItem>(FindObjectsSortMode.None);

        foreach (PickupItem fieldItem in fieldItems)
        {
            if (string.IsNullOrEmpty(fieldItem.ItemId) || !fieldItem.ItemId.StartsWith(ClueItemIdPrefix, System.StringComparison.Ordinal))
            {
                // 단서가 아닌 아이템은 무시
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
