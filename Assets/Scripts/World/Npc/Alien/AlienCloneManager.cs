using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// 라운드 타이머가 일정 시간(검거 투표 등으로 멈춰있는
// 동안은 제외) 줄어들 때마다 여러 마리를 한 번에 스폰한다. 스폰 위치는 접속한 플레이어들 중 본부에 없는 플레이어끼리 비교해
// 가장 고립된 플레이어 주변으로 정하고, 그런 플레이어가 없으면(전원 본부에 있음) 맵 임의 위치로 스폰한다.
// 스폰한 개체의 Died 이벤트를 구독해 HP가 0이 되면 실제로 디스폰시킨다.
public class AlienCloneManager : MonoBehaviour
{
    [Header("스폰 설정 (임시 기본값, 추후 밸런싱 이슈로 조정)")]
    [SerializeField] private GameObject _alienClonePrefab;
    [SerializeField] private MapRegionController _mapRegionController;
    [SerializeField, Min(1)] private int _spawnCountPerCycle = 3;
    [SerializeField, Min(0.1f)] private float _spawnInterval = 60f; // 라운드 타이머가 이만큼(초) 줄어들 때마다 스폰
    [SerializeField, Min(0f)] private float _spawnDistanceFromPlayer = 10f;
    [SerializeField, Min(0.01f)] private float _navMeshSampleDistance = 2f;

    private readonly List<AlienCloneHealth> _aliveClones = new();
    // 지난 프레임에 읽은 라운드 잔여시간. 이번 프레임과의 차이로 "실제로 흐른 라운드 시간"을 계산하는 기준값.
    private float _lastRoundRemainingTime;
    private float _elapsedSinceLastSpawn;

    private void Start()
    {
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
        }
    }

    private void OnDestroy()
    {
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }
    }

    // 서버에서만 매 프레임 마릿수/라운드 타이머 경과량을 확인해 필요하면 새로 스폰한다.
    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (RoundManager.Instance == null) return;

        _aliveClones.RemoveAll(clone => clone == null);

        // Time.deltaTime 대신 라운드 잔여시간의 감소량을 쓴다: 검거 투표 등으로 RoundManager가
        // 타이머를 멈추면 GetRemainingTime()도 같이 안 줄어들어서 elapsed가 0이 되고, 스폰도 같이 멈춘다.
        // Mathf.Max(0f, ...)는 다음 라운드 시작 때 잔여시간이 갑자기 확 늘어나는 순간(음수 elapsed)을 막기 위함.
        float currentRemaining = RoundManager.Instance.GetRemainingTime();
        float elapsed = Mathf.Max(0f, _lastRoundRemainingTime - currentRemaining);
        _lastRoundRemainingTime = currentRemaining;

        _elapsedSinceLastSpawn += elapsed;
        if (_elapsedSinceLastSpawn < _spawnInterval) return;

        // _spawnCountPerCycle마리가 전부 스폰될 때까지 재시도한다(무한 루프 방지용 시도 횟수 상한 포함).
        // 전부 채웠을 때만 타이머를 리셋하고, 못 채웠으면 다음 프레임에 이어서 재시도한다.
        int spawnedCount = 0;
        int attemptLimit = _spawnCountPerCycle * 10;

        for (int attempt = 0; spawnedCount < _spawnCountPerCycle && attempt < attemptLimit; attempt++)
        {
            if (SpawnClone())
            {
                spawnedCount++;
            }
        }

        if (spawnedCount == _spawnCountPerCycle)
        {
            _elapsedSinceLastSpawn = 0f;
        }
    }

    // 스폰 위치를 찾아 외계인 복제체를 네트워크 오브젝트로 스폰한다. 성공 여부를 반환한다.
    private bool SpawnClone()
    {
        if (_alienClonePrefab == null) return false;
        if (!TryGetSpawnPosition(out Vector3 spawnPosition)) return false;

        GameObject instance = Instantiate(_alienClonePrefab, spawnPosition, Quaternion.identity);

        if (!instance.TryGetComponent(out NetworkObject networkObject) ||
            !instance.TryGetComponent(out AlienCloneHealth health))
        {
            Debug.LogError("[AlienCloneManager] 외계인 프리팹에 NetworkObject 또는 AlienCloneHealth가 없습니다.", this);
            Destroy(instance);
            return false;
        }

        networkObject.Spawn(destroyWithScene: true);
        _aliveClones.Add(health);
        // Died는 HP가 0이 될 때 딱 한 번만 발동되고 그 직후 곧바로 디스폰(파괴)되므로,
        // 별도로 구독 해제를 하지 않아도 이 델리게이트가 계속 남아있을 일이 없다.
        health.Died += () => HandleCloneDied(health, networkObject);
        return true;
    }

    // 가장 고립된 현장 플레이어 주변, 무작위 방향으로 일정 거리 떨어진 NavMesh 위 지점을 찾는다.
    // 현장에 있는 플레이어가 없으면(전원 본부에 있음) 맵 임의 위치로 대신한다.
    private bool TryGetSpawnPosition(out Vector3 spawnPosition)
    {
        if (TryFindMostIsolatedFieldPlayerPosition(out Vector3 targetPosition))
        {
            float randomAngle = Random.Range(0f, 360f);
            Vector3 offset = Quaternion.Euler(0f, randomAngle, 0f) * Vector3.forward * _spawnDistanceFromPlayer;
            Vector3 candidate = targetPosition + offset;

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, _navMeshSampleDistance, NavMesh.AllAreas))
            {
                spawnPosition = hit.position;
                return true;
            }
        }

        if (_mapRegionController != null && _mapRegionController.TryGetRandomSpawnPoint(out _, out spawnPosition))
        {
            return true;
        }

        spawnPosition = default;
        return false;
    }

    // 접속한 플레이어 중 본부에 없는 플레이어들끼리 비교해, 가장 가까운 동료와의 거리가 가장 먼
    // (가장 고립된) 플레이어의 위치를 찾는다.
    private bool TryFindMostIsolatedFieldPlayerPosition(out Vector3 position)
    {
        position = default;

        List<Vector3> playerPositions = new();
        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClients.Values)
        {
            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null) continue;
            if (playerObject.TryGetComponent(out PlayerHealth health) && health.IsInHeadquarters) continue;

            playerPositions.Add(playerObject.transform.position);
        }

        if (playerPositions.Count == 0) return false;

        // 플레이어마다 "가장 가까운 다른 플레이어와의 거리"를 구한 뒤, 그 값이 제일 큰(=제일 외딴) 플레이어를 고른다.
        int mostIsolatedIndex = 0;
        float maxNearestDistance = -1f;

        for (int i = 0; i < playerPositions.Count; i++)
        {
            float nearestDistance = float.MaxValue;

            for (int j = 0; j < playerPositions.Count; j++)
            {
                if (i == j) continue;

                float distance = Vector3.Distance(playerPositions[i], playerPositions[j]);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                }
            }

            // 여기까지 구한 nearestDistance가 "i번 플레이어의 가장 가까운 동료와의 거리".
            if (nearestDistance > maxNearestDistance)
            {
                maxNearestDistance = nearestDistance;
                mostIsolatedIndex = i;
            }
        }

        position = playerPositions[mostIsolatedIndex];
        return true;
    }

    // HP가 0이 된 외계인 복제체를 목록에서 빼고 실제로 디스폰시킨다.
    private void HandleCloneDied(AlienCloneHealth health, NetworkObject networkObject)
    {
        _aliveClones.Remove(health);

        if (networkObject != null && networkObject.IsSpawned)
        {
            networkObject.Despawn(destroy: true);
        }
    }

    // 라운드가 끝나면(InRound를 벗어나면) 지금까지 스폰된 외계인 복제체를 전부 강제로 디스폰시킨다.
    private void HandleRoundStateChanged(RoundState state)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (state == RoundState.InRound) return;

        foreach (AlienCloneHealth health in _aliveClones)
        {
            if (health != null && health.TryGetComponent(out NetworkObject networkObject) && networkObject.IsSpawned)
            {
                networkObject.Despawn(destroy: true);
            }
        }

        _aliveClones.Clear();
    }
}
