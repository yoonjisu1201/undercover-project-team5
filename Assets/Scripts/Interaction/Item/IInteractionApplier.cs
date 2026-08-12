using UnityEngine;

// 다른 상호작용 대상에 개입하는 소지 효과형 아이템이 구현한다 (예: RefillPack -> 카트).
public interface IInteractionApplier
{
    // 조준 중일 때 보여줄 문구.
    string InteractionApplyText { get; }

    // 적용 완료 시 보여줄 문구.
    string ApplyCompletedMessage { get; }

    // 판정만 한다 - 부작용 없음. CanInteract에는 절대 관여하지 않는다(그건 대상 자신의 몫).
    // false + failReason => 지금은 적용 못 한다(이유 표시). false + null => 처리 안 함.
    bool CanApplyTo(GameObject user, InteractableBase target, out string failReason);

    // 서버 전용. 대상에 효과를 적용하고, 소모할지 여부도 스스로 정한다 (예: inventory.TryRemoveSelectedItemOnServer 호출).
    void ApplyToOnServer(GameObject user, InteractableBase target, PlayerInventory inventory, int selectedIndex);
}
