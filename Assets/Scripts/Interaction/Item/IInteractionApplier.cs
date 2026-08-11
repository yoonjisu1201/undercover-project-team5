using UnityEngine;

// 다른 상호작용 대상에 개입하는 소지 효과형 아이템이 구현한다 (예: RefillPack -> 카트).
// 첫 구현체(RefillPack)는 이 리팩터링 스코프 밖 - #389에서 별도로 만든다.
public interface IInteractionApplier
{
    // 조준 중일 때 보여줄 문구.
    string InteractionApplyText { get; }

    // 개입 후 아이템 자신이 소모되는지.
    bool ConsumedOnApply { get; }

    // 판정만 한다 - 부작용 없음. CanInteract에는 절대 관여하지 않는다(그건 대상 자신의 몫).
    bool CanApplyTo(GameObject user, InteractableBase target, out string message);

    // 서버 전용. 대상에 효과를 적용하고, ConsumedOnApply면 자기 소모(인벤토리 제거)까지 스스로 처리한다.
    void ApplyToOnServer(GameObject user, InteractableBase target, PlayerInventory inventory, int selectedIndex);
}
