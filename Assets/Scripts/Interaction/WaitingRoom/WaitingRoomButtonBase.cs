using DG.Tweening;
using UnityEngine;

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

        // GalleryButton의 로컬 -Z 방향이 스테이션 안쪽이다.
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
        StopPressAnimation();
        _buttonTransform.localPosition = _releasedButtonLocalPosition;
    }
}
