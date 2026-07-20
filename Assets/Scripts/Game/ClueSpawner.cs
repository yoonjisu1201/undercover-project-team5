using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// 해금된 구역의 Ground 위에 8개 단서를 서버 권한으로 생성하는 클래스
public sealed class ClueSpawner : MonoBehaviour
{
    private const int RequiredClueCount = 8;    // 단서 갯수

    [Header("단서 데이터")]
    [SerializeField] private ItemData[] _clues;

    [Header("스폰 영역")]
    [SerializeField] private ClueSpawnArea[] _spawnAreas;
    [SerializeField] private LayerMask _groundLayer;

    [Header("배치 설정")]
    [SerializeField, Min(1)] private int _maxAttemptsPerClue = 50;
    [SerializeField, Min(0f)] private float _raycastHeight = 10f;
    [SerializeField, Min(0f)] private float _raycastDistance = 30f;
    [SerializeField, Min(0f)] private float _surfaceOffset = 0.04f;
    [SerializeField, Min(0f)] private float _minimumClueDistance = 5f;
    [SerializeField, Min(0f)] private float _maximumClueDistance = 40f;
    [SerializeField, Min(0.01f)] private float _navMeshSampleDistance = 1f;

    private readonly List<Vector3> _spawnedPositions = new();
    private bool _hasSpawned;

    //--- 네트워크 씬 로드 이벤트 등록 및 해제 ---//
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
        // 모든 클라이언트가 현재 씬 로드를 마친 뒤 서버에서 한 번만 생성한다.
        if (_hasSpawned || sceneName != gameObject.scene.name || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        SpawnClues();
    }

    public void SpawnClues()
    {
        // 서버 권한 및 인스펙터 설정 검증
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !ValidateSettings())
        {
            return;
        }

        List<ClueSpawnArea> unlockedAreas = GetUnlockedAreas();
        if (unlockedAreas.Count == 0)
        {
            Debug.LogError("[ClueSpawner] 해금된 단서 스폰 영역이 없습니다.", this);
            return;
        }

        _hasSpawned = true;
        _spawnedPositions.Clear();

        //--- 단서별 랜덤 위치 선정 및 네트워크 스폰 ---//
        foreach (ItemData clueData in _clues)
        {
            if (!TryFindSpawnPosition(unlockedAreas, out Vector3 spawnPosition))
            {
                Debug.LogWarning($"[ClueSpawner] '{clueData.ItemId}'의 스폰 위치를 찾지 못했습니다.", this);
                continue;
            }

            GameObject clueObject = Instantiate(clueData.WorldPrefab, spawnPosition, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            if (!clueObject.TryGetComponent(out PickupItem pickupItem) || !clueObject.TryGetComponent(out NetworkObject networkObject))
            {
                Debug.LogError($"[ClueSpawner] '{clueData.WorldPrefab.name}'에 PickupItem 또는 NetworkObject가 없습니다.", this);
                Destroy(clueObject);
                continue;
            }

            pickupItem.Configure(clueData);
            // 서버에서 지정한 단서 번호로 획득 처리한 뒤 모든 클라이언트에 오브젝트를 생성한다.
            networkObject.Spawn(destroyWithScene: true);
            _spawnedPositions.Add(spawnPosition);
        }
    }

    //--- 8개 단서 데이터와 공용 월드 프리팹 검증 ---//
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

        if (_spawnAreas == null || _spawnAreas.Length == 0 || _groundLayer.value == 0)
        {
            Debug.LogError("[ClueSpawner] 스폰 영역과 Ground 레이어를 설정해야 합니다.", this);
            return false;
        }

        foreach (ClueSpawnArea area in _spawnAreas)
        {
            if (area == null || !area.IsConfigured)
            {
                Debug.LogError("[ClueSpawner] 모든 스폰 영역에 MapBlockController와 MapBlock을 연결해야 합니다.", this);
                return false;
            }
        }

        return true;
    }

    //--- 현재 활성화되고 해금된 영역만 선별 ---//
    private List<ClueSpawnArea> GetUnlockedAreas()
    {
        var unlockedAreas = new List<ClueSpawnArea>();

        foreach (ClueSpawnArea area in _spawnAreas)
        {
            if (area != null && area.IsUnlocked)
            {
                unlockedAreas.Add(area);
            }
        }

        return unlockedAreas;
    }

    private bool TryFindSpawnPosition2(List<ClueSpawnArea> areas, out Vector3 position)
    {
        //--- 해금된 영역에서 스폰 가능한 바닥 위치 탐색 ---//
        for (int attempt = 0; attempt < _maxAttemptsPerClue; attempt++)
        {

            ClueSpawnArea area = areas[Random.Range(0, areas.Count)];
            if (!area.TryGetRandomNavMeshPoint(
                    _navMeshSampleDistance,
                    _maximumClueDistance,
                    out Vector3 navMeshPoint))
            {
                continue;
            }
            position = navMeshPoint + Vector3.up * _surfaceOffset;
            // Vector3 rayOrigin = navMeshPoint + Vector3.up * _raycastHeight;
            // if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, _raycastDistance, _groundLayer, QueryTriggerInteraction.Ignore))
            // {
            //     continue;
            // }

            // position = hit.point + hit.normal * _surfaceOffset;
            return true;
        }

        position = default;
        return false;
    }

    private bool TryFindSpawnPosition(List<ClueSpawnArea> areas, out Vector3 position)
    {
        //--- 영역 안의 임의 지점에서 Ground 레이어 탐색 ---//
        float minimumDistanceSquared = _minimumClueDistance * _minimumClueDistance;
        Vector3 bestCandidate = default;
        float bestNearestDistanceSquared = -1f;

        for (int attempt = 0; attempt < _maxAttemptsPerClue; attempt++)
        {
            ClueSpawnArea area = areas[Random.Range(0, areas.Count)];
            if (!area.TryGetRandomNavMeshPoint(
                    _navMeshSampleDistance,
                    _maximumClueDistance,
                    out Vector3 navMeshPoint))
            {
                continue;
            }

            Vector3 rayOrigin = navMeshPoint + Vector3.up * _raycastHeight;

            // Raycast로 Ground 레이어를 탐색하여 단서가 놓일 위치를 결정한다.
            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, _raycastDistance, _groundLayer, QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            Vector3 candidate = hit.point + hit.normal * _surfaceOffset;
            if (_spawnedPositions.Count == 0)
            {
                position = candidate;
                return true;
            }

            //--- 여러 후보 중 기존 단서들과 가장 멀리 떨어진 위치 선택 ---//
            float nearestDistanceSquared = float.MaxValue;
            foreach (Vector3 spawnedPosition in _spawnedPositions)
            {
                float distanceSquared = (spawnedPosition - candidate).sqrMagnitude;
                nearestDistanceSquared = Mathf.Min(nearestDistanceSquared, distanceSquared);

            }

            if (nearestDistanceSquared >= minimumDistanceSquared &&
                nearestDistanceSquared > bestNearestDistanceSquared)
            {
                bestCandidate = candidate;
                bestNearestDistanceSquared = nearestDistanceSquared;
            }
        }

        if (bestNearestDistanceSquared >= 0f)
        {
            position = bestCandidate;
            return true;
        }

        position = default;
        return false;
    }
}
