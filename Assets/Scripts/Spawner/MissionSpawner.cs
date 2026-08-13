using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Box Collider로 나눈 구역의 통합 NavMesh 위에 7개 미션 머신을 서버 권한으로 생성하는 클래스
public sealed class MissionSpawner : MonoBehaviour
{
    private const int RequiredMissionMachineCount = 7;

    [Header("미션 머신 데이터")]
    [SerializeField] private MissionMachineData[] _MissionMachine;

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
            MissionMachineData missionMachineData = _MissionMachine[index];

            // CCTV 담당 기계는 현재 해방된 지역의 CCTV 수만큼 깔고, 각 기계에 1번부터 담당 CCTV를 나눠 준다.
            int machineCount = missionMachineData.SpawnPerCctv ? GetActiveCctvCount() : 1;
            for (int machineIndex = 0; machineIndex < machineCount; machineIndex++)
            {
                SpawnMissionMachine(missionMachineData, machineIndex + 1);
            }
        }

        SpawnContaminatedSample();

        // CCTV 미션은 CCTV 수만큼 여러 대가 깔리므로, 스폰 수가 미션 종류 수와 같지 않다.
        Debug.Log($"[MissionSpawner] 미션 머신 {_spawnedMachines.Count}개 스폰 완료. (미션 종류 {RequiredMissionMachineCount}개)", this);
    }

    // 미션 머신 한 대를 배치하고 담당 번호를 심어 스폰합니다.
    private void SpawnMissionMachine(MissionMachineData missionMachineData, int targetNumber)
    {
        if (!TryFindSpawnPose(out Vector3 spawnPosition, out Quaternion spawnRotation))
        {
            Debug.LogWarning($"[MissionSpawner] '{missionMachineData.WorldPrefab.name}'의 스폰 위치를 찾지 못했습니다.", this);
            return;
        }

        GameObject missionMachineObject = Instantiate(
            missionMachineData.WorldPrefab,
            spawnPosition,
            spawnRotation);

        if (!missionMachineObject.TryGetComponent(out NetworkObject networkObject))
        {
            Debug.LogError($"[MissionSpawner] '{missionMachineData.WorldPrefab.name}'에 NetworkObject가 없습니다.", this);
            Destroy(missionMachineObject);
            return;
        }

        if (!missionMachineObject.TryGetComponent(out MissionInteractable mission))
        {
            Debug.LogError($"[MissionSpawner] '{missionMachineData.WorldPrefab.name}'에 MissionInteractable이 없습니다.", this);
            Destroy(missionMachineObject);
            return;
        }

        mission.ConfigureCompletionReward(missionMachineData.CompletionReward);
        mission.ConfigureTargetNumber(targetNumber);
        networkObject.Spawn(destroyWithScene: true);
        _spawnedMachines.Add(networkObject);
        _spawnedPositions.Add(spawnPosition);
        Debug.Log($"[MissionSpawner] '{missionMachineData.WorldPrefab.name}' {targetNumber}번 스폰 완료: {spawnPosition}", this);
    }

    // 지역 해방 시 CCTVHub가 그 지역 포인트로 목록을 다시 채우므로, 스폰 시점의 CCTV 수를 그대로 사용합니다.
    private int GetActiveCctvCount()
    {
        CCTVHub cctvHub = FindFirstObjectByType<CCTVHub>();
        if (cctvHub == null || cctvHub.CameraCount == 0)
        {
            Debug.LogError("[MissionSpawner] 활성화된 CCTV가 없어 CCTV 미션 기계를 1대만 생성합니다.", this);
            return 1;
        }

        return cctvHub.CameraCount;
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

    // 기존 장치를 정리하고 현재 해방된 지역을 기준으로 7개 장치를 다시 생성합니다.
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

        foreach (MissionMachineData missionMachineData in _MissionMachine)
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
