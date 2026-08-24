using UnityEngine;

public sealed class WaitingRoomExitInteractable : InteractableBase
{
    private WaitingRoomUI _roomUI;

    public override string InteractionText => "방 나가기";
    public override bool CanInteract(GameObject interactor) => true;

    protected override void Awake()
    {
        base.Awake();
        _roomUI = FindFirstObjectByType<WaitingRoomUI>();
    }

    public override void Interact(GameObject interactor)
    {
        _roomUI.HandleLeaveButtonClicked();
    }
}
