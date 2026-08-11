using UnityEngine;

// 현장에서 채취한 오염 샘플. 들고 미션 기계를 바라보면 투입(분석 시작)할 수 있다.
public class ContaminatedSample : ItemBase, IInteractionApplier
{
    [SerializeField, Min(0.1f)] private float _applyHoldDuration = 1.2f;

    public bool ConsumedOnApply => true;
    public float ApplyHoldDuration => _applyHoldDuration;

    public bool CanApplyTo(GameObject user, InteractableBase target, out string message)
    {
        message = null;
        return target is MissionInteractable mission
            && !mission.IsRequiredItemInserted
            && mission.RequiredItemId == ItemId;
    }

    public void ApplyToOnServer(GameObject user, InteractableBase target)
    {
        if (target is MissionInteractable mission)
        {
            mission.MarkRequiredItemInsertedOnServer();
        }
    }
}
