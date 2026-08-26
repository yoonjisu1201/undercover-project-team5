using UnityEngine;

// 대기방 출구 문을 기존 월드 상호작용 대상으로 만들고 WaitingRoomUI의 퇴장 흐름에 연결한다.
// Canvas 버튼과 별도 퇴장 로직을 만들지 않아 세션 종료 처리를 한 곳에서 유지한다.
public sealed class WaitingRoomExitInteractable : InteractableBase
{
    [SerializeField, Min(0f)] private float _interactHoldDuration = 1.2f;

    private WaitingRoomUI _roomUI;

    public override float InteractHoldThreshold => _interactHoldDuration;
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
