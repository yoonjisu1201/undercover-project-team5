using UnityEngine;

public sealed class NicknameWorldButton : WaitingRoomButtonBase
{
    public override string InteractionText => "닉네임 변경";
    public override bool CanInteract(GameObject interactor) => true;

    protected override void ExecuteButtonAction()
    {
        RoomUI.OnNicknameChangeButtonClicked();
    }
}
