using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// 외계인 복제체의 배회/추적 AI. 서버 권한으로만 동작한다.
// 루트에 붙은 감지용 SphereCollider(Is Trigger) 범위 안에 있는 플레이어를 계속 추적 목록으로 들고 있다가,
// 다운되지 않고 본부 안전구역에 있지 않은 플레이어를 타겟으로 쫓는다. 타겟이 무효화되면(범위 이탈/다운/본부 진입)
// 같은 범위 안에 남아있는 다른 유효한 플레이어를 바로 이어서 타겟으로 잡는다.
[RequireComponent(typeof(NavMeshAgent))]
public class AlienCloneController : NetworkBehaviour
{
    // Animator 파라미터: AlienAnimator의 이동 Bool과 이름이 일치해야 한다.
    private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");

    [Header("배회 설정")]
    [SerializeField, Min(0f)] private float _wanderRadius = 15f;
    [SerializeField, Min(0.01f)] private float _navMeshSampleDistance = 1f;
    [SerializeField, Min(0f)] private float _minWanderDistance = 4f;
    [SerializeField, Min(1)] private int _wanderSampleAttempts = 8;
    [SerializeField, Min(0f)] private float _wanderSpeed = 1.6f;

    [Header("추적 설정")]
    [SerializeField, Min(0f)] private float _chaseStartDistance = 30f;
    [SerializeField, Min(0f)] private float _chaseKeepDistance = 35f;
    [SerializeField, Min(0f)] private float _targetStopDistance = 1.4f;
    [SerializeField, Min(0f)] private float _targetResumeDistance = 1.8f;
    [SerializeField, Min(0f)] private float _chaseSpeed = 3.5f;
    [SerializeField, Min(0f)] private float _chaseFailureTimeout = 2.5f;
    [SerializeField, Min(0f)] private float _lostTargetRetryDelay = 2f;
    [SerializeField] private LayerMask _lineOfSightBlockers = ~0;

    private NavMeshAgent _agent;
    // Alien_main 루트의 Animator를 캐싱해 이동 상태를 IsRunning 파라미터에 반영한다.
    // Animator Controller 연결 자체는 Alien_main 프리팹 Inspector에서 설정된다.
    private Animator _animator;
    private Vector3 _spawnPosition;
    private MapRegion _currentRegion;
    private PlayerHealth _currentTarget;
    private PlayerHealth _ignoredTarget;
    private readonly List<PlayerHealth> _playersInRange = new();
    private readonly Dictionary<PlayerHealth, int> _playerOverlapCounts = new();
    private NavMeshPath _targetPath;
    // 디버그 메뉴에서 범인과 함께 정지시켰을 때, 추적·배회를 모두 멈춘다.
    private bool _isFrozen;
    private bool _isDead;
    private float _chaseFailureTimer;
    private float _lostTargetRetryTimer;

    // AlienCloneAttack이 사거리 판정에 쓸 수 있도록 현재 타겟을 읽기 전용으로 노출한다.
    public PlayerHealth CurrentTarget => _currentTarget;

    // 네트워크 스폰 시 같은 루트의 NavMeshAgent와 Animator를 캐싱하고 스폰 위치를 배회 기준점으로 저장한다.
    // 프리팹의 NetworkAnimator가 이 Animator와 IsRunning 파라미터를 클라이언트에 동기화한다.
    public override void OnNetworkSpawn()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
        _targetPath = new NavMeshPath();
        _spawnPosition = transform.position;
        _currentRegion = FindCurrentRegion();

        if (IsServer && _agent != null)
        {
            _agent.avoidancePriority = Random.Range(35, 66);
            _agent.stoppingDistance = _targetStopDistance;
        }

        // 서버가 아닌 인스턴스는 NavMeshAgent가 스스로 Transform을 갱신하지 않게 해서
        // NetworkTransform이 동기화한 값과 충돌하지 않게 한다.
        if (!IsServer && _agent != null)
        {
            _agent.updatePosition = false;
            _agent.updateRotation = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        ClearPlayersInRange();
        _currentTarget = null;
    }

    // 이동을 멈추거나 다시 풀어준다. 서버에서만 호출된다.
    public void SetFrozen(bool frozen)
    {
        if (_isDead) return;

        _isFrozen = frozen;

        if (_agent == null || !_agent.isOnNavMesh) return;

        if (frozen)
        {
            _agent.ResetPath();
        }

        _agent.isStopped = frozen;
    }

    // 사망 상태의 이동 종료 정책은 컨트롤러가 단일하게 소유한다.
    public void StopForDeath()
    {
        _isDead = true;
        ClearCurrentTarget(true);
        ClearPlayersInRange();

        if (_animator != null)
        {
            _animator.SetBool(IsRunningHash, false);
            _animator.SetBool(IsWalkingHash, false);
        }

        if (_agent == null || !_agent.isOnNavMesh) return;

        _agent.ResetPath();
        _agent.isStopped = true;
    }

    // 서버에서만 매 프레임 실행되는 AI 루프. 타겟이 있으면 추적하고, 없으면 배회한다.
    private void Update()
    {
        // ① 실행 자격 체크: 서버가 아니거나, 아직 스폰 안 됐거나, NavMesh 위에 없으면 아무것도 안 함
        if (!IsServer || !IsSpawned || _isDead || _agent == null || !_agent.isOnNavMesh) return;

        UpdateMovementAnimation();
        UpdateIgnoredTargetTimer();

        // ①-1 정지 상태면 목적지를 새로 잡지 않는다 (디버그 메뉴의 범인 정지와 함께 걸린 상태)
        if (_isFrozen) return;

        if (_currentRegion != null && !_currentRegion.Contains(transform.position))
        {
            ClearCurrentTarget(true);
        }

        // ② 기존 타겟 무효화: 다운/본부 진입/구역 이탈/추적 유지 거리 이탈 시 더 이상 쫓지 않음
        if (_currentTarget != null &&
            (!IsValidTarget(_currentTarget) ||
             !IsInsideCurrentRegion(_currentTarget.transform.position) ||
             !HasLineOfSightToTarget(_currentTarget) ||
             !IsPlayerWithinDistance(_currentTarget, _chaseKeepDistance) ||
             !CanReachTarget(_currentTarget)))
        {
            ClearCurrentTarget(true);
        }

        // ③ 새 타겟 채우기: 타겟이 없으면 범위 안 목록에서 대체 타겟을 찾음
        if (_currentTarget == null)
        {
            _currentTarget = FindValidTargetInRange();
        }

        // ④ 타겟 있으면 추적하고 끝: 배회 로직으로 안 내려감
        if (_currentTarget != null)
        {
            if (ShouldHoldNearTarget())
            {
                HoldNearTarget();
                return;
            }

            if (IsChaseFailing())
            {
                IgnoreTargetTemporarily(_currentTarget);
                ClearCurrentTarget(true);
                return;
            }

            _agent.speed = _chaseSpeed;
            _agent.isStopped = false;
            _agent.SetDestination(_currentTarget.transform.position);
            return;
        }

        // ⑤ 배회 목적지 도착 안 함: 경로 계산 중이거나 아직 도착 전이면 대기
        if (_agent.pathPending || _agent.remainingDistance > _agent.stoppingDistance)
        {
            return;
        }

        // ⑥ 새 배회 목적지: 도착했으면 다음 랜덤 목적지를 뽑아서 이동
        if (TryGetRandomWanderDestination(out Vector3 destination))
        {
            _agent.speed = _wanderSpeed;
            _agent.isStopped = false;
            _agent.SetDestination(destination);
        }
    }

    // 감지 범위 안으로 들어온 플레이어를 추적 후보 목록에 추가한다.
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || _isDead) return;

        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null)
        {
            return;
        }

        if (_playerOverlapCounts.TryGetValue(playerHealth, out int overlapCount))
        {
            _playerOverlapCounts[playerHealth] = overlapCount + 1;
            return;
        }

        _playerOverlapCounts.Add(playerHealth, 1);

        if (!_playersInRange.Contains(playerHealth))
        {
            AddPlayerInRange(playerHealth);
        }
    }

    // 감지 범위를 벗어난 플레이어를 목록에서 빼고, 현재 타겟이었다면 타겟도 해제한다.
    private void OnTriggerExit(Collider other)
    {
        if (!IsServer || _isDead) return;

        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null) return;

        if (!_playerOverlapCounts.TryGetValue(playerHealth, out int overlapCount))
        {
            return;
        }

        overlapCount--;
        if (overlapCount > 0)
        {
            _playerOverlapCounts[playerHealth] = overlapCount;
            return;
        }

        _playerOverlapCounts.Remove(playerHealth);
        RemovePlayerInRange(playerHealth);

        if (playerHealth == _currentTarget)
        {
            ClearCurrentTarget(true);
        }
    }

    // 범위 안 목록 중 사라진 대상만 정리하면서, 추적 시작 거리 안의 가장 가까운 유효 플레이어를 찾는다.
    private PlayerHealth FindValidTargetInRange()
    {
        for (int i = _playersInRange.Count - 1; i >= 0; i--)
        {
            PlayerHealth player = _playersInRange[i];
            if (player == null)
            {
                _playersInRange.RemoveAt(i);
            }
        }

        PlayerHealth nearestPlayer = null;
        float nearestDistanceSqr = _chaseStartDistance * _chaseStartDistance;

        foreach (PlayerHealth player in _playersInRange)
        {
            if (!IsValidTarget(player))
            {
                continue;
            }

            if (player == _ignoredTarget)
            {
                continue;
            }

            Vector3 offset = player.transform.position - transform.position;
            offset.y = 0f;

            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr <= nearestDistanceSqr &&
                IsInsideCurrentRegion(player.transform.position) &&
                HasLineOfSightToTarget(player) &&
                CanReachTarget(player))
            {
                nearestPlayer = player;
                nearestDistanceSqr = distanceSqr;
            }
        }

        return nearestPlayer;
    }

    // 지금 추적 대상으로 쓸 수 있는 플레이어인지 판단한다 (살아있고, 다운되지 않았고, 본부에 없어야 함).
    private bool IsValidTarget(PlayerHealth target)
    {
        return target != null && !target.IsDowned && !target.IsInHeadquarters;
    }

    private bool HasLineOfSightToTarget(PlayerHealth target)
    {
        if (target == null)
        {
            return false;
        }

        Vector3 origin = transform.position + Vector3.up;
        Vector3 targetPosition = target.transform.position + Vector3.up;

        Vector3 direction = targetPosition - origin;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction.normalized,
            direction.magnitude,
            _lineOfSightBlockers,
            QueryTriggerInteraction.Ignore);

        if (hits.Length == 0)
        {
            return true;
        }

        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            if (ShouldIgnoreLineOfSightHit(hit.collider))
            {
                continue;
            }

            return hit.collider.GetComponentInParent<PlayerHealth>() == target;
        }

        return true;
    }

    private bool ShouldIgnoreLineOfSightHit(Collider hitCollider)
    {
        if (hitCollider == null || hitCollider.transform.IsChildOf(transform))
        {
            return true;
        }

        // 미션 장치의 실제 Collider는 플레이어 상호작용에는 필요하지만, Alien의 시야를 끊으면 추적이 부자연스럽게 멈춘다.
        return hitCollider.GetComponentInParent<MissionInteractable>() != null ||
               hitCollider.GetComponentInParent<BreakerLeverInteractable>() != null;
    }

    private bool CanReachTarget(PlayerHealth target)
    {
        if (target == null || _agent == null || !_agent.isOnNavMesh)
        {
            return false;
        }

        if (!NavMesh.CalculatePath(transform.position, target.transform.position, NavMesh.AllAreas, _targetPath))
        {
            return false;
        }

        return _targetPath.status == NavMeshPathStatus.PathComplete;
    }

    private bool IsInsideCurrentRegion(Vector3 position)
    {
        return _currentRegion == null || _currentRegion.Contains(position);
    }

    private bool IsPlayerWithinDistance(PlayerHealth player, float distance)
    {
        if (player == null)
        {
            return false;
        }

        Vector3 offset = player.transform.position - transform.position;
        offset.y = 0f;

        return offset.sqrMagnitude <= distance * distance;
    }

    private bool ShouldHoldNearTarget()
    {
        if (_currentTarget == null)
        {
            return false;
        }

        float stopDistance = _agent.isStopped ? _targetResumeDistance : _targetStopDistance;
        Vector3 offset = _currentTarget.transform.position - transform.position;
        offset.y = 0f;

        return offset.sqrMagnitude <= stopDistance * stopDistance;
    }

    private void HoldNearTarget()
    {
        if (_agent.hasPath)
        {
            _agent.ResetPath();
        }

        _agent.isStopped = true;
        _chaseFailureTimer = 0f;
    }

    private bool IsChaseFailing()
    {
        if (_currentTarget == null || _agent.pathPending)
        {
            return false;
        }

        bool failed =
            _agent.pathStatus == NavMeshPathStatus.PathInvalid ||
            _agent.pathStatus == NavMeshPathStatus.PathPartial ||
            (!_agent.hasPath && !ShouldHoldNearTarget());

        if (!failed)
        {
            _chaseFailureTimer = 0f;
            return false;
        }

        _chaseFailureTimer += Time.deltaTime;
        return _chaseFailureTimer >= _chaseFailureTimeout;
    }

    private void UpdateMovementAnimation()
    {
        if (_animator == null)
        {
            return;
        }

        bool isMoving =
            !_agent.isStopped &&
            _agent.hasPath &&
            _agent.remainingDistance > _agent.stoppingDistance;

        bool isRunning = isMoving && _currentTarget != null;
        bool isWalking = isMoving && _currentTarget == null;

        _animator.SetBool(IsRunningHash, isRunning);
        _animator.SetBool(IsWalkingHash, isWalking);
    }

    // 스폰 위치 기준 반경 안에서 걸어갈 수 있는 랜덤 NavMesh 지점을 하나 찾는다.
    private bool TryGetRandomWanderDestination(out Vector3 destination)
    {
        float minDistanceSqr = _minWanderDistance * _minWanderDistance;

        for (int i = 0; i < _wanderSampleAttempts; i++)
        {
            Vector3 randomPoint = GetRandomWanderCandidate();

            if ((randomPoint - transform.position).sqrMagnitude < minDistanceSqr)
            {
                continue;
            }

            if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, _navMeshSampleDistance, NavMesh.AllAreas) &&
                IsInsideCurrentRegion(hit.position))
            {
                destination = hit.position;
                return true;
            }
        }

        destination = default;
        return false;
    }

    private Vector3 GetRandomWanderCandidate()
    {
        if (_currentRegion != null && _currentRegion.Bounds != null)
        {
            BoxCollider bounds = _currentRegion.Bounds;
            Vector3 halfSize = bounds.size * 0.5f;
            Vector3 localPoint = bounds.center + new Vector3(
                Random.Range(-halfSize.x, halfSize.x),
                0f,
                Random.Range(-halfSize.z, halfSize.z));

            Vector3 worldPoint = bounds.transform.TransformPoint(localPoint);
            worldPoint.y = transform.position.y;
            return worldPoint;
        }

        Vector2 randomCircle = Random.insideUnitCircle * _wanderRadius;
        return _spawnPosition + new Vector3(randomCircle.x, 0f, randomCircle.y);
    }

    private MapRegion FindCurrentRegion()
    {
        MapRegionController regionController = FindFirstObjectByType<MapRegionController>();
        if (regionController != null && regionController.TryGetUnlockedRegionAt(transform.position, out MapRegion unlockedRegion))
        {
            return unlockedRegion;
        }

        MapRegion[] regions = FindObjectsByType<MapRegion>(FindObjectsSortMode.None);
        foreach (MapRegion region in regions)
        {
            if (region != null && region.IsUnlocked && region.Contains(transform.position))
            {
                return region;
            }
        }

        foreach (MapRegion region in regions)
        {
            if (region != null && region.Contains(transform.position))
            {
                return region;
            }
        }

        return null;
    }

    private void HandlePlayerDownedStateChanged(bool previousValue, bool newValue)
    {
        if (!IsServer)
        {
            return;
        }

        if (newValue)
        {
            if (_currentTarget != null && _currentTarget.IsDowned)
            {
                ClearCurrentTarget(true);
            }

            return;
        }

        if (_currentTarget == null)
        {
            _currentTarget = FindValidTargetInRange();
        }
    }

    private void ClearCurrentTarget(bool resetPath)
    {
        _currentTarget = null;
        _chaseFailureTimer = 0f;

        if (!resetPath || _agent == null || !_agent.isOnNavMesh)
        {
            return;
        }

        _agent.ResetPath();
        _agent.isStopped = _isFrozen;
    }

    private void IgnoreTargetTemporarily(PlayerHealth target)
    {
        _ignoredTarget = target;
        _lostTargetRetryTimer = _lostTargetRetryDelay;
    }

    private void UpdateIgnoredTargetTimer()
    {
        if (_ignoredTarget == null)
        {
            return;
        }

        _lostTargetRetryTimer -= Time.deltaTime;
        if (_lostTargetRetryTimer > 0f)
        {
            return;
        }

        _ignoredTarget = null;
    }

    private void ClearPlayersInRange()
    {
        foreach (PlayerHealth player in _playersInRange)
        {
            if (player != null)
            {
                player.DownedStateChanged -= HandlePlayerDownedStateChanged;
            }
        }

        _playersInRange.Clear();
        _playerOverlapCounts.Clear();
    }

    private void AddPlayerInRange(PlayerHealth player)
    {
        if (player == null || _playersInRange.Contains(player))
        {
            return;
        }

        _playersInRange.Add(player);
        player.DownedStateChanged += HandlePlayerDownedStateChanged;
    }

    private void RemovePlayerInRange(PlayerHealth player)
    {
        if (player == null)
        {
            return;
        }

        if (_playersInRange.Remove(player))
        {
            player.DownedStateChanged -= HandlePlayerDownedStateChanged;
        }
    }
}
