using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

// 외계인 복제체의 서버 권한 근접 공격을 관리한다.
// 현재 타겟이 사거리 안에 들어오면 이동을 멈추고 공격 애니메이션을 실행한다.
// 실제 데미지는 공격 클립의 Animation Event가 활성화한 오른손 HitBox 충돌로 적용된다.
[RequireComponent(typeof(AlienCloneController))]
[RequireComponent(typeof(AlienCloneHealth))]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(NetworkAnimator))]
public class AlienCloneAttack : NetworkBehaviour
{
    // Animator 파라미터: AlienAnimator의 Attack Trigger와 이름이 일치해야 한다.
    private static readonly int AttackHash = Animator.StringToHash("Attack");

    [Header("공격 설정")]
    [SerializeField, Min(0f)] private float _attackRange = 2f;
    [SerializeField, Min(0.01f)] private float _attackCooldown = 1.5f;
    [SerializeField, Min(0.01f)] private float _attackStateTimeout = 1.2f;
    [SerializeField, Min(0f)] private float _attackTurnSpeed = 720f;
    // Inspector 연결: Alien_main 프리팹의 RightHandAttackHitbox 컴포넌트를 할당한다.
    [SerializeField] private AlienAttackHitbox _attackHitbox;

    private AlienCloneController _controller;
    // Alien_main 루트에서 공격 중 사용할 체력, 이동, 네트워크 애니메이션 컴포넌트를 캐싱한다.
    // _isAttacking은 Animation Event로 공격이 완료될 때까지 다음 공격을 막는다.
    private AlienCloneHealth _health;
    private NavMeshAgent _agent;
    private NetworkAnimator _networkAnimator;
    private bool _isAttacking;
    private float _attackStateTimer;
    private float _cooldownTimer;   // 공격 쿨다운 타이머
    private bool _isDiedSubscribed; // 서버에서만 죽었는지 확인하는 변수

    // Alien_main 프리팹의 동일한 루트 GameObject에 있는 컴포넌트를 가져온다.
    // 오른손 자식 오브젝트의 _attackHitbox만 Inspector에서 직접 연결한다.
    private void Awake()
    {
        _controller = GetComponent<AlienCloneController>();
        _health = GetComponent<AlienCloneHealth>();
        _agent = GetComponent<NavMeshAgent>();
        _networkAnimator = GetComponent<NetworkAnimator>();

        if (_attackHitbox == null)
        {
            _attackHitbox = GetComponentInChildren<AlienAttackHitbox>(true);
        }
    }

    // 서버에서만 체력의 Died 이벤트를 구독한다.
    // HP가 0이 되면 공격 애니메이션 진행 여부와 관계없이 HandleDied가 먼저 실행된다.
    public override void OnNetworkSpawn()
    {
        if (!IsServer || _health == null)
        {
            return;
        }

        _health.Died += HandleDied;
        _isDiedSubscribed = true;
    }

    // 네트워크 디스폰 시 Died 이벤트 구독을 해제해 제거된 개체에 콜백이 남지 않게 한다.
    public override void OnNetworkDespawn()
    {
        if (!_isDiedSubscribed || _health == null)
        {
            return;
        }

        _health.Died -= HandleDied;
        _isDiedSubscribed = false;
    }

    // 서버에서 살아있는 외계인만 현재 타겟, 공격 상태, 쿨다운, 사거리를 순서대로 검사한다.
    private void Update()
    {
        if (!IsServer || !IsSpawned || _health == null || _health.IsDowned || _controller == null)
        {
            return;
        }

        // 타겟 선정과 유효성 관리는 AlienCloneController가 담당한다.
        PlayerHealth target = _controller.CurrentTarget;
        if (target == null)
        {
            return;
        }

        UpdateAttackStateTimer();

        _cooldownTimer -= Time.deltaTime;
        if (_isAttacking || _cooldownTimer > 0f)
        {
            return;
        }

        if (!IsTargetInAttackRange(target.transform.position))
        {
            return;
        }

        if (_networkAnimator == null)
        {
            Debug.LogWarning("[AlienCloneAttack] NetworkAnimator가 없어 공격 애니메이션을 실행할 수 없습니다.", this);
            return;
        }

        // 사거리 안에 들어오면 이동을 멈춘 뒤 NetworkAnimator로 Attack Trigger를 동기화한다.
        _isAttacking = true;
        _attackStateTimer = _attackStateTimeout;
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
        }

        FaceTarget(target.transform.position);
        _networkAnimator.SetTrigger(AttackHash);
        _cooldownTimer = _attackCooldown;

        // 사거리 판정은 공격 시작에만 사용한다.
        // 실제 데미지는 아래 Animation Event 메서드와 AlienAttackHitbox가 처리한다.
    }

    // Animation Event 진입점: Right Hook 클립의 타격 시작 프레임에서 문자열로 직접 호출된다.
    // C# 호출 참조가 없어 보여도 제거하거나 이름을 변경하면 안 된다.
    public void EnableAttackHitbox()
    {
        if (!IsServer)
        {
            return;
        }

        if (_attackHitbox == null)
        {
            Debug.LogWarning("[AlienCloneAttack] 공격 Hitbox가 연결되지 않아 타격 판정을 시작할 수 없습니다.", this);
            return;
        }

        _attackHitbox.BeginSwing();
    }

    // Animation Event 진입점: Right Hook 클립의 타격 종료 프레임에서 문자열로 직접 호출된다.
    // 서버만 실제 공격 Collider를 비활성화한다.
    public void DisableAttackHitbox()
    {
        if (!IsServer)
        {
            return;
        }

        _attackHitbox?.EndSwing();
    }

    // Animation Event 진입점: Right Hook 클립의 마지막 구간에서 문자열로 직접 호출된다.
    // 공격 상태를 해제하고, 살아있는 경우에만 NavMeshAgent 이동을 다시 허용한다.
    public void CompleteAttack()
    {
        if (!IsServer || _health == null || _health.IsDowned)
        {
            return;
        }

        _isAttacking = false;
        _attackStateTimer = 0f;
    }

    // AlienCloneHealth.Died 이벤트 처리.
    // 사망 애니메이션이 시작되기 전에 진행 중인 공격 판정과 이동·추적을 모두 중단한다.
    private void HandleDied()
    {
        if (!IsServer)
        {
            return;
        }

        _isAttacking = false;
        _attackStateTimer = 0f;
        _attackHitbox?.EndSwing();
        _controller?.StopForDeath();
    }

    private void UpdateAttackStateTimer()
    {
        if (!_isAttacking)
        {
            return;
        }

        _attackStateTimer -= Time.deltaTime;
        if (_attackStateTimer > 0f)
        {
            return;
        }

        _isAttacking = false;
        _attackHitbox?.EndSwing();
    }

    // NavMeshAgent가 추적하는 평면과 동일하게 높이 차이를 제외한 XZ 거리로 사거리를 판단한다.
    private bool IsTargetInAttackRange(Vector3 targetPosition)
    {
        Vector3 offset = targetPosition - transform.position;
        offset.y = 0f;

        return offset.sqrMagnitude <= _attackRange * _attackRange;
    }

    private void FaceTarget(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        if (_attackTurnSpeed <= 0f)
        {
            transform.rotation = targetRotation;
            return;
        }

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            _attackTurnSpeed * Time.deltaTime);
    }
}
