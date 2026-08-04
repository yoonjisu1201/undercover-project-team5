using Unity.Netcode;
using UnityEngine;

// 외계인 복제체의 근접 공격. AlienCloneController가 들고 있는 현재 타겟에게
// 사거리 안에 들어오면 쿨다운마다 반복해서 데미지를 입힌다. 서버 권한으로만 동작한다.
[RequireComponent(typeof(AlienCloneController))]
public class AlienCloneAttack : NetworkBehaviour
{
    [Header("공격 설정")]
    [SerializeField, Min(0f)] private float _attackRange = 2f;
    [SerializeField, Min(0.01f)] private float _attackCooldown = 1.5f;

    private AlienCloneController _controller;
    private float _cooldownTimer;

    private void Awake()
    {
        _controller = GetComponent<AlienCloneController>();
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned) return;

        PlayerHealth target = _controller.CurrentTarget;
        if (target == null) return;

        _cooldownTimer -= Time.deltaTime;
        if (_cooldownTimer > 0f) return;

        float distance = Vector3.Distance(transform.position, target.transform.position);
        if (distance > _attackRange) return;

        target.TakeAlienAttackDamage();
        _cooldownTimer = _attackCooldown;
    }
}
