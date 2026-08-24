using DG.Tweening;
using UnityEngine;

// 준비·닉네임 월드 버튼이 공유하는 눌림 연출과 WaitingRoomUI 연결을 담당한다.
// 실제 기능은 파생 클래스에 맡기되 Interact 흐름은 고정해 모든 버튼이 같은 입력 피드백을 거치게 한다.
public abstract class WaitingRoomButtonBase : InteractableBase
{
    [Header("버튼 눌림 연출")]
    [SerializeField] private Transform _buttonTransform;
    [SerializeField, Min(0f)] private float _pressDepth = 0.025f;
    [SerializeField, Min(0f)] private float _pressDuration = 0.08f;

    private Vector3 _releasedButtonLocalPosition;
    private Tween _pressTween;

    protected WaitingRoomUI RoomUI { get; private set; }

    protected override void Awake()
    {
        base.Awake();

        RoomUI = FindFirstObjectByType<WaitingRoomUI>();
        _releasedButtonLocalPosition = _buttonTransform.localPosition;
    }

    public sealed override void Interact(GameObject interactor)
    {
        PlayPressAnimation();
        ExecuteButtonAction();
    }

    protected abstract void ExecuteButtonAction();

    private void PlayPressAnimation()
    {
        StopPressAnimation();

        // 레이아웃마다 버튼 회전이 달라도 표면 안쪽으로 눌리도록 로컬 -Z를 부모 좌표의 이동 방향으로 변환한다.
        Vector3 pressDirection = _buttonTransform.localRotation * Vector3.back;
        Vector3 pressedPosition = _releasedButtonLocalPosition + pressDirection * _pressDepth;

        Sequence sequence = DOTween.Sequence();
        sequence.Append(_buttonTransform.DOLocalMove(pressedPosition, _pressDuration).SetEase(Ease.OutQuad));

        sequence.Append(
            _buttonTransform.DOLocalMove(_releasedButtonLocalPosition, _pressDuration).SetEase(Ease.OutQuad)
            );

        _pressTween = sequence;
    }

    private void StopPressAnimation()
    {
        _pressTween?.Kill();
        _pressTween = null;
    }

    protected virtual void OnDisable()
    {
        // 눌리는 도중 오브젝트가 꺼져도 다음 활성화 때 중간 위치에 남지 않도록 연출과 위치를 함께 복구한다.
        StopPressAnimation();
        _buttonTransform.localPosition = _releasedButtonLocalPosition;
    }
}
