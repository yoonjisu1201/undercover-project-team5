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
    // Animator 파라미터: AlienAnimator의 IsRunning Bool과 이름이 일치해야 한다.
    private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");

    [Header("배회 설정")]
    [SerializeField, Min(0f)] private float _wanderRadius = 15f;
    [SerializeField, Min(0.01f)] private float _navMeshSampleDistance = 1f;

    private NavMeshAgent _agent;
    // Alien_main 루트의 Animator를 캐싱해 이동 상태를 IsRunning 파라미터에 반영한다.
    // Animator Controller 연결 자체는 Alien_main 프리팹 Inspector에서 설정된다.
    private Animator _animator;
    private Vector3 _spawnPosition;
    private PlayerHealth _currentTarget;
    private readonly List<PlayerHealth> _playersInRange = new();
    // 디버그 메뉴에서 범인과 함께 정지시켰을 때, 추적·배회를 모두 멈춘다.
    private bool _isFrozen;
    private bool _isDead;

    // AlienCloneAttack이 사거리 판정에 쓸 수 있도록 현재 타겟을 읽기 전용으로 노출한다.
    public PlayerHealth CurrentTarget => _currentTarget;

    // 네트워크 스폰 시 같은 루트의 NavMeshAgent와 Animator를 캐싱하고 스폰 위치를 배회 기준점으로 저장한다.
    // 프리팹의 NetworkAnimator가 이 Animator와 IsRunning 파라미터를 클라이언트에 동기화한다.
    public override void OnNetworkSpawn()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
        _spawnPosition = transform.position;

        // 서버가 아닌 인스턴스는 NavMeshAgent가 스스로 Transform을 갱신하지 않게 해서
        // NetworkTransform이 동기화한 값과 충돌하지 않게 한다.
        if (!IsServer)
        {
            _agent.updatePosition = false;
            _agent.updateRotation = false;
        }
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
        _currentTarget = null;
        _playersInRange.Clear();

        if (_animator != null)
        {
            _animator.SetBool(IsRunningHash, false);
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

        // Agent가 정지되지 않았고 유효한 경로를 따라 목적지로 이동 중일 때만 Run 상태로 전환한다.
        // 공격으로 Agent가 정지되거나 목적지에 도착하면 false가 되어 Idle 상태로 복귀한다.
        bool isRunning =
            !_agent.isStopped &&
            _agent.hasPath &&
            _agent.remainingDistance > _agent.stoppingDistance;

        if (_animator != null)
        {
            _animator.SetBool(IsRunningHash, isRunning);
        }

        // ①-1 정지 상태면 목적지를 새로 잡지 않는다 (디버그 메뉴의 범인 정지와 함께 걸린 상태)
        if (_isFrozen) return;

        // ② 기존 타겟 무효화: 다운되거나 본부에 들어갔으면 더 이상 쫓지 않음
        if (_currentTarget != null && !IsValidTarget(_currentTarget))
        {
            _currentTarget = null;
        }

        // ③ 새 타겟 채우기: 타겟이 없으면 범위 안 목록에서 대체 타겟을 찾음
        if (_currentTarget == null)
        {
            _currentTarget = FindValidTargetInRange();
        }

        // ④ 타겟 있으면 추적하고 끝: 배회 로직으로 안 내려감
        if (_currentTarget != null)
        {
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
            _agent.SetDestination(destination);
        }
    }

    // 감지 범위 안으로 들어온 플레이어를 추적 후보 목록에 추가한다.
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || _isDead) return;

        if (other.TryGetComponent(out PlayerHealth playerHealth) && !_playersInRange.Contains(playerHealth))
        {
            _playersInRange.Add(playerHealth);
        }
    }

    // 감지 범위를 벗어난 플레이어를 목록에서 빼고, 현재 타겟이었다면 타겟도 해제한다.
    private void OnTriggerExit(Collider other)
    {
        if (!IsServer || _isDead) return;

        if (!other.TryGetComponent(out PlayerHealth playerHealth)) return;

        _playersInRange.Remove(playerHealth);

        if (playerHealth == _currentTarget)
        {
            _currentTarget = null;
        }
    }

    // 범위 안 목록 중 죽었거나 사라진 대상을 정리하면서, 지금 쫓을 수 있는 첫 번째 유효한 플레이어를 찾는다.
    private PlayerHealth FindValidTargetInRange()
    {
        _playersInRange.RemoveAll(player => player == null);

        foreach (PlayerHealth player in _playersInRange)
        {
            if (IsValidTarget(player))
            {
                return player;
            }
        }

        return null;
    }

    // 지금 추적 대상으로 쓸 수 있는 플레이어인지 판단한다 (살아있고, 다운되지 않았고, 본부에 없어야 함).
    private bool IsValidTarget(PlayerHealth target)
    {
        return target != null && !target.IsDowned && !target.IsInHeadquarters;
    }

    // 스폰 위치 기준 반경 안에서 걸어갈 수 있는 랜덤 NavMesh 지점을 하나 찾는다.
    private bool TryGetRandomWanderDestination(out Vector3 destination)
    {
        Vector3 randomPoint = _spawnPosition + Random.insideUnitSphere * _wanderRadius;

        if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, _navMeshSampleDistance, NavMesh.AllAreas))
        {
            destination = hit.position;
            return true;
        }

        destination = default;
        return false;
    }
}
