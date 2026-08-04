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
    [Header("배회 설정")]
    [SerializeField, Min(0f)] private float _wanderRadius = 15f;
    [SerializeField, Min(0.01f)] private float _navMeshSampleDistance = 1f;

    private NavMeshAgent _agent;
    private Vector3 _spawnPosition;
    private PlayerHealth _currentTarget;
    private readonly List<PlayerHealth> _playersInRange = new();

    // AlienCloneAttack이 사거리 판정에 쓸 수 있도록 현재 타겟을 읽기 전용으로 노출한다.
    public PlayerHealth CurrentTarget => _currentTarget;

    // 네트워크 스폰 시 NavMeshAgent를 캐싱하고 스폰 위치를 배회 기준점으로 저장한다.
    public override void OnNetworkSpawn()
    {
        _agent = GetComponent<NavMeshAgent>();
        _spawnPosition = transform.position;

        // 서버가 아닌 인스턴스는 NavMeshAgent가 스스로 Transform을 갱신하지 않게 해서
        // NetworkTransform이 동기화한 값과 충돌하지 않게 한다.
        if (!IsServer)
        {
            _agent.updatePosition = false;
            _agent.updateRotation = false;
        }
    }

    // 서버에서만 매 프레임 실행되는 AI 루프. 타겟이 있으면 추적하고, 없으면 배회한다.
    private void Update()
    {
        // ① 실행 자격 체크: 서버가 아니거나, 아직 스폰 안 됐거나, NavMesh 위에 없으면 아무것도 안 함
        if (!IsServer || !IsSpawned || !_agent.isOnNavMesh) return;

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
        if (!IsServer) return;

        if (other.TryGetComponent(out PlayerHealth playerHealth) && !_playersInRange.Contains(playerHealth))
        {
            _playersInRange.Add(playerHealth);
        }
    }

    // 감지 범위를 벗어난 플레이어를 목록에서 빼고, 현재 타겟이었다면 타겟도 해제한다.
    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;

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
