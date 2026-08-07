using System.Collections.Generic;
using UnityEngine;

// Alien_main 프리팹의 오른손 뼈에 부착되는 근접 공격 판정 컴포넌트.
// AlienCloneAttack의 Animation Event 처리 메서드가 공격 유효 구간에만 Collider를 활성화한다.
// 한 번의 스윙에서 동일 플레이어에게 데미지가 중복 적용되지 않도록 타격 기록을 관리한다.
[RequireComponent(typeof(Collider))]
public class AlienAttackHitbox : MonoBehaviour
{
    // 공격 중 활성화할 Collider와 이번 스윙에서 이미 타격한 플레이어를 보관한다.
    // HashSet은 한 플레이어의 여러 Collider가 접촉해도 데미지를 한 번만 적용하기 위해 사용한다.
    private Collider _hitbox;
    private readonly HashSet<PlayerHealth> _hitPlayers = new();
    private bool _isSwingActive;

    // 같은 GameObject의 Collider를 가져와 런타임 초기 상태를 비활성화한다.
    // 프리팹에서도 비활성화되어 있지만 코드에서도 공격 전 비활성 상태를 보장한다.
    private void Awake()
    {
        _hitbox = GetComponent<Collider>();
        if (_hitbox == null)
        {
            Debug.LogError("[AlienAttackHitbox] 공격 판정 Collider를 찾을 수 없습니다.", this);
            enabled = false;
            return;
        }

        _hitbox.enabled = false;
    }

    // 공격 시작마다 이전 타격 기록을 비운 뒤 오른손 Collider를 활성화한다.
    // AlienCloneAttack.EnableAttackHitbox에서 호출된다.
    public void BeginSwing()
    {
        if (_hitbox == null)
        {
            return;
        }

        _hitPlayers.Clear();
        _isSwingActive = true;
        _hitbox.enabled = true;
    }

    // 공격 유효 구간 종료 또는 외계인 사망 시 오른손 Collider를 즉시 비활성화한다.
    public void EndSwing()
    {
        _isSwingActive = false;

        if (_hitbox == null)
        {
            return;
        }

        _hitbox.enabled = false;
    }

    // 플레이어 감지용 Trigger는 제외하고 실제 비-Trigger Collider와의 접촉만 공격으로 판정한다.
    private void OnTriggerEnter(Collider other)
    {
        if (!_isSwingActive || other.isTrigger)
        {
            return;
        }

        // 자식 Collider에 접촉해도 부모의 PlayerHealth를 찾아 동일한 플레이어 단위로 판정한다.
        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();

        // 공격할 수 없는 대상과 이번 스윙에서 이미 맞은 플레이어는 제외한다.
        if (playerHealth == null || playerHealth.IsDowned || playerHealth.IsInHeadquarters || !_hitPlayers.Add(playerHealth))
        {
            return;
        }

        playerHealth.TakeAlienAttackDamage();
    }
}
