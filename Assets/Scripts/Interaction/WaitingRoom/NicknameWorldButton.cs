using UnityEngine;

// 기존 닉네임 변경 UI를 월드 버튼 입력으로 연결한다.
// 닉네임 처리 로직을 중복하지 않고 WaitingRoomUI를 단일 기능 진입점으로 유지한다.
public sealed class NicknameWorldButton : WaitingRoomButtonBase
{
    public override string InteractionText => "닉네임 변경";
    public override bool CanInteract(GameObject interactor) => true;

    protected override void ExecuteButtonAction()
    {
        RoomUI.OnNicknameChangeButtonClicked();
    }
}
