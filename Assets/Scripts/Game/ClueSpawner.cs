using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Box Collider로 나눈 구역의 통합 NavMesh 위에 8개 단서를 서버 권한으로 생성하는 클래스
public sealed class ClueSpawner : MonoBehaviour
{
    private const int RequiredClueCount = 8;

    [Header("단서 데이터")]
    [SerializeField] private ItemData[] _clues;

    [Header("스폰 영역")]
    [SerializeField] private MapRegionController _regionController;
    [SerializeField] private LayerMask _groundLayer;

    [Header("배치 설정")]
    [SerializeField, Min(1)] private int _maxAttemptsPerClue = 50;
    [SerializeField, Min(0f)] private float _raycastHeight = 10f;
    [SerializeField, Min(0f)] private float _raycastDistance = 30f;
    [SerializeField, Min(0f)] private float _surfaceOffset = 0.04f;
    [SerializeField, Min(0f)] private float _minimumClueDistance = 5f;

    private readonly List<Vector3> _spawnedPositions = new();
    private bool _hasSpawned;

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

        SpawnClues();
    }

    public void SpawnClues()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !ValidateSettings())
        {
            return;
        }

        if (!_regionController.RefreshSpawnAreas())
        {
            Debug.LogError("[ClueSpawner] NavMesh가 포함된 단서 스폰 영역이 없습니다.", this);
            return;
        }

        _hasSpawned = true;
        _spawnedPositions.Clear();

        foreach (ItemData clueData in _clues)
        {
            if (!TryFindSpawnPosition(out Vector3 spawnPosition))
            {
                Debug.LogWarning($"[ClueSpawner] '{clueData.ItemId}'의 스폰 위치를 찾지 못했습니다.", this);
                continue;
            }

            GameObject clueObject = Instantiate(
                clueData.WorldPrefab,
                spawnPosition,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            if (!clueObject.TryGetComponent(out PickupItem pickupItem) ||
                !clueObject.TryGetComponent(out NetworkObject networkObject))
            {
                Debug.LogError($"[ClueSpawner] '{clueData.WorldPrefab.name}'에 PickupItem 또는 NetworkObject가 없습니다.", this);
                Destroy(clueObject);
                continue;
            }

            pickupItem.Configure(clueData);
            networkObject.Spawn(destroyWithScene: true);
            _spawnedPositions.Add(spawnPosition);
        }
    }

    private bool ValidateSettings()
    {
        if (_clues == null || _clues.Length != RequiredClueCount)
        {
            Debug.LogError($"[ClueSpawner] 단서 데이터는 정확히 {RequiredClueCount}개가 필요합니다.", this);
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

        if (_regionController == null || _groundLayer.value == 0)
        {
            Debug.LogError("[ClueSpawner] MapRegionController와 Ground 레이어를 설정해야 합니다.", this);
            return false;
        }

        return true;
    }

    private bool TryFindSpawnPosition(out Vector3 position)
    {
        float minimumDistanceSquared = _minimumClueDistance * _minimumClueDistance;

        for (int attempt = 0; attempt < _maxAttemptsPerClue; attempt++)
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
            if (IsFarEnoughFromSpawnedClues(candidate, minimumDistanceSquared))
            {
                position = candidate;
                return true;
            }
        }

        position = default;
        return false;
    }

    private bool IsFarEnoughFromSpawnedClues(Vector3 candidate, float minimumDistanceSquared)
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
