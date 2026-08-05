using Unity.Netcode;
// ===== [검토표시-추가-시작] =====
using Unity.Netcode.Components;
// ===== [검토표시-추가-끝] =====
using UnityEngine;
// ===== [검토표시-추가-시작] =====
using UnityEngine.AI;
// ===== [검토표시-추가-끝] =====

// ===== [검토표시-수정-시작] =====
// 외계인 복제체의 근접 공격. AlienCloneController가 들고 있는 현재 타겟이 사거리 안에 들어오면
// 공격 애니메이션을 실행하고, 애니메이션의 타격 구간에만 오른손 HitBox를 활성화한다. 서버 권한으로만 동작한다.
// ===== [검토표시-수정-끝] =====
[RequireComponent(typeof(AlienCloneController))]
public class AlienCloneAttack : NetworkBehaviour
{
    // ===== [검토표시-추가-시작] =====
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    // ===== [검토표시-추가-끝] =====

    [Header("공격 설정")]
    [SerializeField, Min(0f)] private float _attackRange = 2f;
    [SerializeField, Min(0.01f)] private float _attackCooldown = 1.5f;
    // ===== [검토표시-추가-시작] =====
    [SerializeField] private AlienAttackHitbox _attackHitbox;
    // ===== [검토표시-추가-끝] =====

    private AlienCloneController _controller;
    // ===== [검토표시-추가-시작] =====
    private AlienCloneHealth _health;
    private NavMeshAgent _agent;
    private NetworkAnimator _networkAnimator;
    private bool _isAttacking;
    // ===== [검토표시-추가-끝] =====
    private float _cooldownTimer;

    private void Awake()
    {
        _controller = GetComponent<AlienCloneController>();
        // ===== [검토표시-추가-시작] =====
        _health = GetComponent<AlienCloneHealth>();
        _agent = GetComponent<NavMeshAgent>();
        _networkAnimator = GetComponent<NetworkAnimator>();
        // ===== [검토표시-추가-끝] =====
    }

    // ===== [검토표시-추가-시작] =====
    public override void OnNetworkSpawn()
    {
        _health.Died += HandleDied;
    }

    public override void OnNetworkDespawn()
    {
        _health.Died -= HandleDied;
    }
    // ===== [검토표시-추가-끝] =====

    private void Update()
    {
        // ===== [검토표시-수정-시작] =====
        if (!IsServer || !IsSpawned || _health.IsDowned)
        {
            return;
        }

        PlayerHealth target = _controller.CurrentTarget;
        if (target == null)
        {
            return;
        }

        _cooldownTimer -= Time.deltaTime;
        if (_isAttacking || _cooldownTimer > 0f)
        {
            return;
        }

        float distance = Vector3.Distance(transform.position, target.transform.position);
        if (distance > _attackRange)
        {
            return;
        }

        _isAttacking = true;
        _agent.isStopped = true;
        _networkAnimator.SetTrigger(AttackHash);
        _cooldownTimer = _attackCooldown;
        // ===== [검토표시-수정-끝] =====
        // ===== [검토표시-제거] 기존 사거리 진입 즉시 데미지를 적용하던 코드 제거 =====
    }

    // ===== [검토표시-추가-시작] =====
    public void EnableAttackHitbox()
    {
        if (!IsServer)
        {
            return;
        }

        _attackHitbox.BeginSwing();
    }

    public void DisableAttackHitbox()
    {
        if (!IsServer)
        {
            return;
        }

        _attackHitbox.EndSwing();
    }

    public void CompleteAttack()
    {
        if (!IsServer || _health.IsDowned)
        {
            return;
        }

        _isAttacking = false;
        _agent.isStopped = false;
    }

    private void HandleDied()
    {
        _isAttacking = false;
        _attackHitbox.EndSwing();
        _agent.isStopped = true;
        _controller.enabled = false;
    }
    // ===== [검토표시-추가-끝] =====
}
