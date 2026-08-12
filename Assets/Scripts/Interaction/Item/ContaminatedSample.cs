using UnityEngine;

// 현장에서 채취한 오염 샘플. 들고 미션 기계를 바라보면 투입(분석 시작)할 수 있다.
public class ContaminatedSample : ItemBase, IInteractionApplier
{
    public string InteractionApplyText => "투입하기";
    public string ApplyCompletedMessage => "샘플 투입 완료";

    public bool CanApplyTo(GameObject user, InteractableBase target, out string failReason)
    {
        failReason = null;
        return target is MissionInteractable mission
            && !mission.IsRequiredItemInserted
            && mission.RequiredItemId == ItemId;
    }

    public void ApplyToOnServer(GameObject user, InteractableBase target, PlayerInventory inventory, int selectedIndex)
    {
        if (target is MissionInteractable mission)
        {
            mission.MarkRequiredItemInsertedOnServer();
        }

        inventory.TryRemoveSelectedItemOnServer(ItemId, selectedIndex);
    }
}
