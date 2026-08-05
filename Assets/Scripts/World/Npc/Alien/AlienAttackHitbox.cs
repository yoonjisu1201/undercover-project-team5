// ===== [검토표시-추가-시작] =====
using System.Collections.Generic;
using UnityEngine;

// 공격 애니메이션의 타격 구간에만 활성화되며, 한 번의 공격에서 같은 플레이어에게 한 번만 데미지를 적용한다.
[RequireComponent(typeof(Collider))]
public class AlienAttackHitbox : MonoBehaviour
{
    private Collider _hitbox;
    private readonly HashSet<PlayerHealth> _hitPlayers = new();

    private void Awake()
    {
        _hitbox = GetComponent<Collider>();
        _hitbox.enabled = false;
    }

    public void BeginSwing()
    {
        _hitPlayers.Clear();
        _hitbox.enabled = true;
    }

    public void EndSwing()
    {
        _hitbox.enabled = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();

        if (playerHealth == null ||
            playerHealth.IsDowned ||
            playerHealth.IsInHeadquarters ||
            !_hitPlayers.Add(playerHealth))
        {
            return;
        }

        playerHealth.TakeAlienAttackDamage();
    }
}
// ===== [검토표시-추가-끝] =====
