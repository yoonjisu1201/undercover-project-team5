using UnityEngine;

// 애니메이션 이벤트를 보스 루트의 BossAttack으로 넘긴다.
//
// 애니메이션 이벤트는 Animator가 붙은 그 GameObject의 컴포넌트만 이름으로 호출한다. 라운드마다
// 외형을 자식으로 만들면서 Animator가 자식으로 내려갔고, BossAttack은 루트에 있어서 이벤트가
// 아무에게도 닿지 않았다("has no receiver"). 그러면 공격 상태가 타임아웃까지 안 풀려 보스가
// 멈춰 서 있고, 타격 판정도 열리지 않는다.
//
// BossVisual이 외형을 만들 때 Animator가 있는 오브젝트에 이 컴포넌트를 붙인다.
// 메서드 이름은 클립의 이벤트 이름과 정확히 같아야 한다.
public class BossAnimationEventRelay : MonoBehaviour
{
    private BossAttack _attack;

    private void Awake()
    {
        _attack = GetComponentInParent<BossAttack>();

        if (_attack == null)
        {
            Debug.LogWarning($"[보스] '{name}' 위쪽에서 BossAttack을 찾지 못해 공격 이벤트가 전달되지 않습니다.", this);
        }
    }

    public void EnableAttackHitbox() => _attack?.EnableAttackHitbox();

    public void DisableAttackHitbox() => _attack?.DisableAttackHitbox();

    public void CompleteAttack() => _attack?.CompleteAttack();
}
