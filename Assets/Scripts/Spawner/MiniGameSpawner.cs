using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Box Collider로 나눈 구역의 통합 NavMesh 위에 9개 미니게임 머신을 서버 권한으로 생성하는 클래스
public sealed class MiniGameSpawner : MonoBehaviour
{
    private const int RequiredMiniGameMachineCount = 9;

    [Header("미니게임 머신 데이터")]
    [SerializeField] private ItemData[] _MiniGameMachine;

    [Header("스폰 영역")]
    [SerializeField] private MapRegionController _regionController;
    [SerializeField] private LayerMask _groundLayer;

    [Header("배치 설정")]
    [SerializeField, Min(1)] private int _maxAttemptsPerMiniGameMachine = 50;
    [SerializeField, Min(0f)] private float _raycastHeight = 10f;
    [SerializeField, Min(0f)] private float _raycastDistance = 30f;
    [SerializeField, Min(0f)] private float _surfaceOffset = 0.04f;
    [SerializeField, Min(0f)] private float _minimumMiniGameMachineDistance = 5f;

    private readonly List<Vector3> _spawnedPositions = new();
    private bool _hasSpawned;

    private void Start()
    {
        // 이 오브젝트가 PlayScene과 함께 생성되면 OnLoadEventCompleted 구독보다
        // 씬 로드 완료 이벤트가 먼저 지나갈 수 있으므로 Start에서도 서버 스폰을 보장한다.
        if (!_hasSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            SpawnMiniGameMachines();
        }
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
        if (_hasSpawned || sceneName != gameObject.scene.name || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        SpawnMiniGameMachines();
    }

    public void SpawnMiniGameMachines()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !ValidateSettings())
        {
            return;
        }

        if (!_regionController.RefreshSpawnAreas())
        {
            Debug.LogError("[MiniGameSpawner] NavMesh가 포함된 미니게임 머신 스폰 영역이 없습니다.", this);
            return;
        }

        _hasSpawned = true;
        _spawnedPositions.Clear();

        for (int index = 0; index < _MiniGameMachine.Length; index++)
        {
            ItemData miniGameMachineData = _MiniGameMachine[index];

            if (!TryFindSpawnPosition(out Vector3 spawnPosition))
            {
                Debug.LogWarning($"[MiniGameSpawner] '{miniGameMachineData.ItemId}'의 스폰 위치를 찾지 못했습니다.", this);
                continue;
            }

            GameObject miniGameMachineObject = Instantiate(
                miniGameMachineData.WorldPrefab,
                spawnPosition,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            if (!miniGameMachineObject.TryGetComponent(out NetworkObject networkObject))
            {
                Debug.LogError($"[MiniGameSpawner] '{miniGameMachineData.WorldPrefab.name}'에 NetworkObject가 없습니다.", this);
                Destroy(miniGameMachineObject);
                continue;
            }

            if (index == 1)
            {
                if (!miniGameMachineObject.TryGetComponent(out PickupItem pickupItem))
                {
                    Debug.LogError($"[MiniGameSpawner] 2번 미니게임 머신 '{miniGameMachineData.WorldPrefab.name}'에 PickupItem이 없습니다.", this);
                    Destroy(miniGameMachineObject);
                    continue;
                }

                pickupItem.Configure(miniGameMachineData);
            }
            else if (!miniGameMachineObject.TryGetComponent(out MiniGameInteractable _))
            {
                Debug.LogError($"[MiniGameSpawner] '{miniGameMachineData.WorldPrefab.name}'에 MiniGameInteractable이 없습니다.", this);
                Destroy(miniGameMachineObject);
                continue;
            }

            networkObject.Spawn(destroyWithScene: true);
            _spawnedPositions.Add(spawnPosition);
            Debug.Log($"[MiniGameSpawner] '{miniGameMachineData.WorldPrefab.name}' 스폰 완료: {spawnPosition}", this);
        }

        Debug.Log($"[MiniGameSpawner] 미니게임 머신 {_spawnedPositions.Count}/{RequiredMiniGameMachineCount}개 스폰 완료.", this);
    }

    private bool ValidateSettings()
    {
        if (_MiniGameMachine == null || _MiniGameMachine.Length != RequiredMiniGameMachineCount)
        {
            Debug.LogError($"[MiniGameSpawner] 미니게임 머신 데이터는 정확히 {RequiredMiniGameMachineCount}개가 필요합니다.", this);
            return false;
        }

        foreach (ItemData miniGameMachineData in _MiniGameMachine)
        {
            if (miniGameMachineData == null || miniGameMachineData.WorldPrefab == null)
            {
                Debug.LogError("[MiniGameSpawner] 비어 있거나 WorldPrefab이 없는 미니게임 머신 데이터가 있습니다.", this);
                return false;
            }
        }

        if (_regionController == null || _groundLayer.value == 0)
        {
            Debug.LogError("[MiniGameSpawner] MapRegionController와 Ground 레이어를 설정해야 합니다.", this);
            return false;
        }

        return true;
    }

    private bool TryFindSpawnPosition(out Vector3 position)
    {
        float minimumDistanceSquared = _minimumMiniGameMachineDistance * _minimumMiniGameMachineDistance;

        for (int attempt = 0; attempt < _maxAttemptsPerMiniGameMachine; attempt++)
        {
            if (!_regionController.TryGetRandomSpawnPoint(out _, out Vector3 navMeshPoint))
            {
                continue;
            }

            Vector3 rayOrigin = navMeshPoint + Vector3.up * _raycastHeight;
            if (!Physics.Raycast(
                    rayOrigin,
                    Vector3.down,
                    out RaycastHit hit,
                    _raycastDistance,
                    _groundLayer,
                    QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            Vector3 candidate = hit.point + hit.normal * _surfaceOffset;
            if (IsFarEnoughFromSpawnedMiniGameMachines(candidate, minimumDistanceSquared))
            {
                position = candidate;
                return true;
            }
        }

        position = default;
        return false;
    }

    private bool IsFarEnoughFromSpawnedMiniGameMachines(Vector3 candidate, float minimumDistanceSquared)
    {
        foreach (Vector3 spawnedPosition in _spawnedPositions)
        {
            if ((spawnedPosition - candidate).sqrMagnitude < minimumDistanceSquared)
            {
                return false;
            }
        }

        return true;
    }
}
