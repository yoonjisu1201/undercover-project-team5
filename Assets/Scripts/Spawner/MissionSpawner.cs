using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Box Collider로 나눈 구역의 통합 NavMesh 위에 8개 미션 머신을 서버 권한으로 생성하는 클래스
public sealed class MissionSpawner : MonoBehaviour
{
    private const int RequiredMissionMachineCount = 8;

    [Header("미션 머신 데이터")]
    [SerializeField] private ItemData[] _MissionMachine;

    [Header("오염 샘플")]
    [SerializeField] private ItemData _contaminatedSample;
    [SerializeField] private GameObject _contaminatedSampleSourcePrefab;

    [Header("스폰 영역")]
    [SerializeField] private MapRegionController _regionController;
    [SerializeField] private LayerMask _groundLayer;

    [Header("배치 설정")]
    [SerializeField, Min(1)] private int _maxAttemptsPerMissionMachine = 50;
    [SerializeField, Min(0f)] private float _raycastHeight = 10f;
    [SerializeField, Min(0f)] private float _raycastDistance = 30f;
    [SerializeField, Min(0f)] private float _surfaceOffset = 0.04f;
    [SerializeField, Min(0f)] private float _minimumMissionMachineDistance = 5f;

    private readonly List<Vector3> _spawnedPositions = new();
    private readonly List<NetworkObject> _spawnedMachines = new();
    private NetworkObject _spawnedSample;
    private NetworkObject _spawnParent;
    private bool _hasSpawned;

    // 런타임에 생성되는 샘플이 MissionSpawner 하위에 동기화되도록 네트워크 부모를 보관합니다.
    private void Awake()
    {
        _spawnParent = GetComponent<NetworkObject>();
        if (_spawnParent == null) { Debug.LogError("[MissionSpawner] 샘플을 하위에 생성하려면 NetworkObject가 필요합니다.", this); }
    }

    private void Start()
    {
        // 이 오브젝트가 PlayScene과 함께 생성되면 OnLoadEventCompleted 구독보다
        // 씬 로드 완료 이벤트가 먼저 지나갈 수 있으므로 Start에서도 서버 스폰을 보장한다.
        if (!_hasSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            SpawnMissionMachines();
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

        SpawnMissionMachines();
    }

    public void SpawnMissionMachines()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !ValidateSettings())
        {
            return;
        }

        if (!_regionController.RefreshSpawnAreas())
        {
            Debug.LogError("[MissionSpawner] NavMesh가 포함된 미션 머신 스폰 영역이 없습니다.", this);
            return;
        }

        _hasSpawned = true;
        _spawnedPositions.Clear();

        for (int index = 0; index < _MissionMachine.Length; index++)
        {
            ItemData missionMachineData = _MissionMachine[index];

            if (!TryFindSpawnPose(out Vector3 spawnPosition, out Quaternion spawnRotation))
            {
                Debug.LogWarning($"[MissionSpawner] '{missionMachineData.ItemId}'의 스폰 위치를 찾지 못했습니다.", this);
                continue;
            }

            GameObject missionMachineObject = Instantiate(
                missionMachineData.WorldPrefab,
                spawnPosition,
                spawnRotation);

            if (!missionMachineObject.TryGetComponent(out NetworkObject networkObject))
            {
                Debug.LogError($"[MissionSpawner] '{missionMachineData.WorldPrefab.name}'에 NetworkObject가 없습니다.", this);
                Destroy(missionMachineObject);
                continue;
            }

            if (!missionMachineObject.TryGetComponent(out MissionInteractable mission))
            {
                Debug.LogError($"[MissionSpawner] '{missionMachineData.WorldPrefab.name}'에 MissionInteractable이 없습니다.", this);
                Destroy(missionMachineObject);
                continue;
            }

            mission.ConfigureCompletionReward(missionMachineData.CompletionReward);
            networkObject.Spawn(destroyWithScene: true);
            _spawnedMachines.Add(networkObject);
            _spawnedPositions.Add(spawnPosition);
            Debug.Log($"[MissionSpawner] '{missionMachineData.WorldPrefab.name}' 스폰 완료: {spawnPosition}", this);
        }

        SpawnContaminatedSample();

        Debug.Log($"[MissionSpawner] 미션 머신 {_spawnedMachines.Count}/{RequiredMissionMachineCount}개 스폰 완료.", this);
    }

    // 해금된 지역의 바닥 한 곳에 오염 샘플을 서버 권한으로 생성합니다.
    private void SpawnContaminatedSample()
    {
        if (_contaminatedSample == null || _contaminatedSampleSourcePrefab == null) { Debug.LogError("[MissionSpawner] 오염 샘플 데이터 또는 현장 프리팹이 없습니다.", this); return; }
        if (!TryFindSpawnPose(out Vector3 spawnPosition, out Quaternion spawnRotation)) { Debug.LogWarning("[MissionSpawner] 오염 샘플의 스폰 위치를 찾지 못했습니다.", this); return; }

        GameObject sampleObject = Instantiate(_contaminatedSampleSourcePrefab, spawnPosition, spawnRotation);
        if (!sampleObject.TryGetComponent(out NetworkObject networkObject)) { Debug.LogError("[MissionSpawner] 오염 샘플 프리팹에 NetworkObject가 없습니다.", this); Destroy(sampleObject); return; }
        if (sampleObject.TryGetComponent(out ContaminatedSampleSource sampleSource)) { sampleSource.Configure(_contaminatedSample); }

        networkObject.Spawn(destroyWithScene: true);
        if (_spawnParent != null && _spawnParent.IsSpawned && !networkObject.TrySetParent(_spawnParent, worldPositionStays: true)) { Debug.LogWarning("[MissionSpawner] 오염 샘플을 MissionSpawner 하위로 설정하지 못했습니다.", this); }
        _spawnedSample = networkObject;
        _spawnedPositions.Add(spawnPosition);
        Debug.Log($"[MissionSpawner] 오염 샘플 스폰 완료: {spawnPosition}", this);
    }

    // 기존 장치를 정리하고 현재 해방된 지역을 기준으로 8개 장치를 다시 생성합니다.
    public void RespawnMissionMachines()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[MissionSpawner] 서버에서만 미션 장치를 재생성할 수 있습니다.", this);
            return;
        }

        foreach (NetworkObject machine in _spawnedMachines)
        {
            if (machine != null && machine.IsSpawned)
            {
                machine.Despawn(destroy: true);
            }
        }

        _spawnedMachines.Clear();
        if (_spawnedSample != null && _spawnedSample.IsSpawned) { _spawnedSample.Despawn(destroy: true); }
        _spawnedSample = null;
        _spawnedPositions.Clear();
        _hasSpawned = false;
        SpawnMissionMachines();
    }

    private bool ValidateSettings()
    {
        if (_MissionMachine == null || _MissionMachine.Length != RequiredMissionMachineCount)
        {
            Debug.LogError($"[MissionSpawner] 미션 머신 데이터는 정확히 {RequiredMissionMachineCount}개가 필요합니다.", this);
            return false;
        }

        foreach (ItemData missionMachineData in _MissionMachine)
        {
            if (missionMachineData == null || missionMachineData.WorldPrefab == null)
            {
                Debug.LogError("[MissionSpawner] 비어 있거나 WorldPrefab이 없는 미션 머신 데이터가 있습니다.", this);
                return false;
            }
        }

        if (_contaminatedSample == null || _contaminatedSample.WorldPrefab == null || _contaminatedSampleSourcePrefab == null)
        {
            Debug.LogError("[MissionSpawner] 오염 샘플 데이터, 드롭 프리팹, 현장 프리팹을 설정해야 합니다.", this);
            return false;
        }

        if (_regionController == null || _groundLayer.value == 0)
        {
            Debug.LogError("[MissionSpawner] MapRegionController와 Ground 레이어를 설정해야 합니다.", this);
            return false;
        }

        return true;
    }

    private bool TryFindSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        float minimumDistanceSquared = _minimumMissionMachineDistance * _minimumMissionMachineDistance;

        for (int attempt = 0; attempt < _maxAttemptsPerMissionMachine; attempt++)
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
            if (IsFarEnoughFromSpawnedMissionMachines(candidate, minimumDistanceSquared))
            {
                position = candidate;
                Quaternion groundAlignment = Quaternion.FromToRotation(Vector3.up, hit.normal);
                Quaternion randomYaw = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);
                rotation = groundAlignment * randomYaw;
                return true;
            }
        }

        position = default;
        rotation = Quaternion.identity;
        return false;
    }

    private bool IsFarEnoughFromSpawnedMissionMachines(Vector3 candidate, float minimumDistanceSquared)
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
