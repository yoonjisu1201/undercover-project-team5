using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// 보스의 근접 공격. 사거리 안이면 공격 애니메이션을 실행하고, 타격 구간에 닿은 사람에게 피해를 준다.
//
// 판정을 손뼈에 붙인 Collider로 하지 않는 이유: 라운드마다 BossVisual이 모델 자식을 교체하므로
// 뼈에 붙인 것은 같이 사라진다. 대신 애니메이션 이벤트로 타격 구간만 열고, 그 동안 보스 앞쪽을
// 겹침 검사한다. 타이밍은 애니메이션이 정하고 판정 위치는 모델과 무관해진다.
//
// 판정은 서버 전용이다. 공격 애니메이션은 RPC로 각 클라이언트가 자기 화면의 Animator에 재생한다.
// 라운드마다 외형이 바뀌고 외형마다 Animator가 달라서 NetworkAnimator로는 동기화할 수 없다.
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(BossVisual))]
public class BossAttack : NetworkBehaviour
{
    // AlienAnimator의 Attack Trigger와 이름이 일치해야 한다.
    private static readonly int AttackHash = Animator.StringToHash("Attack");

    [Header("공격")]
    [Tooltip("이 거리 안에 들어오면 공격을 시작한다. 그래프의 이동 노드는 이보다 가깝게 붙어야 한다.")]
    [SerializeField, Min(0f)] private float _attackRange = 2f;

    [SerializeField, Min(0.01f)] private float _attackCooldown = 2f;

    [Tooltip("애니메이션 이벤트가 오지 않아도 이 시간이 지나면 공격 상태를 푼다. 안 두면 영원히 멈춘다.")]
    [SerializeField, Min(0.01f)] private float _attackStateTimeout = 1.5f;

    [Tooltip("공격 직전 표적을 향해 도는 속도(도/초). 느리면 옆으로 돌아 피할 수 있다.")]
    [SerializeField, Min(0f)] private float _attackTurnSpeed = 540f;

    [Header("타격 판정")]
    [SerializeField, Min(0f)] private float _damage = 40f;

    [Tooltip("판정 구체의 중심. 보스 발밑 기준으로 위/앞으로 얼마나 띄울지.")]
    [SerializeField, Min(0f)] private float _hitHeight = 1.1f;
    [SerializeField, Min(0f)] private float _hitForwardOffset = 0.9f;
    [SerializeField, Min(0f)] private float _hitRadius = 1.1f;

    [Tooltip("판정에 넣을 레이어. 플레이어가 올라가는 레이어만 켜면 벽을 훑지 않는다.")]
    [SerializeField] private LayerMask _hitLayers = ~0;

    private NavMeshAgent _agent;
    private BossVisual _visual;
    private readonly Collider[] _overlapBuffer = new Collider[16];

    // 한 번 휘두를 때 같은 사람이 여러 번 맞지 않게 기록한다.
    private readonly HashSet<PlayerHealth> _hitPlayers = new();

    private bool _isAttacking;
    private bool _isSwingActive;
    private float _attackStateTimer;
    private float _cooldownTimer;

    public bool IsAttacking => _isAttacking;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _visual = GetComponent<BossVisual>();
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        if (_cooldownTimer > 0f)
        {
            _cooldownTimer -= Time.deltaTime;
        }

        UpdateAttackStateTimer();

        if (_isSwingActive)
        {
            ApplySwingDamage();
        }
    }

    public bool IsInAttackRange(Vector3 targetPosition)
    {
        // NavMeshAgent가 움직이는 평면과 같게 높이 차이는 무시한다.
        Vector3 offset = targetPosition - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= _attackRange * _attackRange;
    }

    // 그래프의 공격 노드가 호출한다. 시작했으면 true.
    public bool TryStartAttack(GameObject target)
    {
        if (!IsServer || target == null || _isAttacking || _cooldownTimer > 0f)
        {
            return false;
        }

        // 피해 계산(ApplySwingDamage)에서 한 번 더 거르지만 여기서도 막아야 한다.
        // 안 그러면 쓰러진 사람에게 피해 없는 공격 모션만 계속 나가고, 그동안 agent 가 멈춰 선다.
        if (!SurvivorRegistry.IsActive(target))
        {
            return false;
        }

        if (!IsInAttackRange(target.transform.position))
        {
            return false;
        }

        _isAttacking = true;
        _attackStateTimer = _attackStateTimeout;
        _cooldownTimer = _attackCooldown;

        // 휘두르는 동안 미끄러져 나가지 않게 멈춘다.
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
        }

        FaceTarget(target.transform.position);
        PlayAttackAnimationRpc();
        return true;
    }

    // 타격 판정은 서버가 하지만 모션은 모두가 봐야 한다. 애니메이션 이벤트도 각 클라이언트에서
    // 발생하는데, 이벤트를 받는 메서드들이 서버가 아니면 바로 빠져나오므로 판정은 서버에서만 선다.
    [Rpc(SendTo.Everyone)]
    private void PlayAttackAnimationRpc()
    {
        Animator animator = _visual != null ? _visual.ActiveAnimator : null;
        if (animator == null)
        {
            Debug.LogWarning("[보스 공격] 재생할 Animator를 찾지 못했습니다.", this);
            return;
        }

        animator.SetTrigger(AttackHash);
    }

    #region 애니메이션 이벤트

    // Right Hook 클립의 이벤트가 이름으로 직접 호출한다(0.24초).
    // C# 호출 참조가 없어 보여도 이름을 바꾸거나 지우면 안 된다.
    public void EnableAttackHitbox()
    {
        if (!IsServer)
        {
            return;
        }

        _hitPlayers.Clear();
        _isSwingActive = true;
    }

    // Right Hook 클립의 이벤트(0.39초).
    public void DisableAttackHitbox()
    {
        if (!IsServer)
        {
            return;
        }

        _isSwingActive = false;
    }

    // Right Hook 클립의 이벤트(0.94초). 공격 상태를 풀고 다시 움직일 수 있게 한다.
    public void CompleteAttack()
    {
        if (!IsServer)
        {
            return;
        }

        EndAttack();
    }

    #endregion

    private void ApplySwingDamage()
    {
        Vector3 center = transform.position
            + Vector3.up * _hitHeight
            + transform.forward * _hitForwardOffset;

        int count = Physics.OverlapSphereNonAlloc(
            center, _hitRadius, _overlapBuffer, _hitLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            // 자식 Collider에 닿아도 부모의 PlayerHealth를 찾아 같은 사람으로 묶는다.
            PlayerHealth playerHealth = _overlapBuffer[i].GetComponentInParent<PlayerHealth>();
            if (playerHealth == null || playerHealth.IsDowned || playerHealth.IsInHeadquarters)
            {
                continue;
            }

            if (!_hitPlayers.Add(playerHealth))
            {
                continue;
            }

            // 피격 방향 표시는 손이 아니라 보스 본체를 기준으로 해야 맞은 쪽이 자연스럽다.
            playerHealth.TakeDamage(_damage, transform.position);
        }
    }

    // 애니메이션 이벤트가 오지 않는 경우(클립 교체, 상태 전이 실패 등)의 안전장치.
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

        EndAttack();
    }

    private void EndAttack()
    {
        _isAttacking = false;
        _isSwingActive = false;
        _attackStateTimer = 0f;

        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
        }
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
        transform.rotation = _attackTurnSpeed <= 0f
            ? targetRotation
            : Quaternion.RotateTowards(transform.rotation, targetRotation, _attackTurnSpeed * Time.deltaTime);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(
            transform.position + Vector3.up * _hitHeight + transform.forward * _hitForwardOffset,
            _hitRadius);
    }
}
