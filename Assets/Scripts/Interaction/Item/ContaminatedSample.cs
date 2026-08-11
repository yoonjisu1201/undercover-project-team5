using UnityEngine;

// 현장에서 채취한 오염 샘플. 들고 미션 기계를 바라보면 투입(분석 시작)할 수 있다.
public class ContaminatedSample : ItemBase, IInteractionApplier
{
    public string InteractionApplyText => "투입하기";
    public bool ConsumedOnApply => true;

    public bool CanApplyTo(GameObject user, InteractableBase target, out string message)
    {
        message = null;
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

        if (ConsumedOnApply)
        {
            inventory.TryRemoveSelectedItemOnServer(ItemId, selectedIndex);
        }
    }
}
