using DG.Tweening;
using EPOOutline;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Outlinable))]
public abstract class InteractableBase : NetworkBehaviour, IInteractable
{
    public abstract string InteractionText { get; }
    public abstract bool CanInteract(GameObject interactor);
    public abstract void Interact(GameObject interactor);

    // 화면 중심 조준 판정 반경에 곱해지는 배율. 기본은 1(PlayerInteraction의 공통 반경 그대로 사용).
    public virtual float AimRadiusMultiplier => 1f;

    [Header("외곽선 설정")]
    [SerializeField, Min(0f)] private float _outlineFadeDuration = 0.15f;   // 외곽선 페이드 지속 시간
    private Outlinable _outlinable;

    private Tween _outlineTween;
    private Color _outlineColor;    // 외곽선 색상 저장
    private Color _frontOutlineColor;   // 전면 외곽선 색상 저장
    private Color _backOutlineColor;    // 후면 외곽선 색상 저장
    private float _outlineVisibility;   // 외곽선 투명도

    private void Awake()
    {
        if (_outlinable == null)
        {
            _outlinable = GetComponentInChildren<Outlinable>();
        }

        if (_outlinable == null)
        {
            return;
        }

        _outlineColor = _outlinable.OutlineParameters.Color;    // 외곽선 색상 저장
        _frontOutlineColor = _outlinable.FrontParameters.Color; // 전면 외곽선 색상 저장
        _backOutlineColor = _outlinable.BackParameters.Color;   // 후면 외곽선 색상 저장

        _outlineVisibility = 0f;    // 초기 외곽선 가시성은 0으로 설정
        ApplyOutlineVisibility(_outlineVisibility);
        _outlinable.enabled = false;
    }

    public void SetOutline(bool isVisible)
    {
        if (_outlinable == null)
        {
            return;
        }

        _outlineTween?.Kill();  // 이전 트윈이 존재하면 종료

        if (isVisible)
        {
            _outlinable.enabled = true;
        }

        float targetVisibility = isVisible ? 1f : 0f;   // 목표 외곽선 가시성 설정

        if (_outlineFadeDuration <= 0f) // 외곽선 페이드 지속 시간이 0 이하이면 즉시 적용
        {
            _outlineVisibility = targetVisibility;
            ApplyOutlineVisibility(_outlineVisibility);
            _outlinable.enabled = isVisible;
            return;
        }

        _outlineTween = DOTween.To(() => _outlineVisibility, value =>   // 외곽선 가시성 업데이트
            {
                _outlineVisibility = value;
                ApplyOutlineVisibility(value);
            },
            targetVisibility, _outlineFadeDuration).SetEase(Ease.OutQuad).SetTarget(this);

        if (!isVisible) // 외곽선이 사라진 후 Outlinable을 비활성화
        {
            _outlineTween.OnComplete(() => _outlinable.enabled = false);
        }
    }

    private void ApplyOutlineVisibility(float visibility)
    {
        Color outlineColor = _outlineColor;
        outlineColor.a *= visibility;
        _outlinable.OutlineParameters.Color = outlineColor;

        Color frontOutlineColor = _frontOutlineColor;
        frontOutlineColor.a *= visibility;
        _outlinable.FrontParameters.Color = frontOutlineColor;

        Color backOutlineColor = _backOutlineColor;
        backOutlineColor.a *= visibility;
        _outlinable.BackParameters.Color = backOutlineColor;
    }

    public override void OnDestroy()
    {
        _outlineTween?.Kill();
        base.OnDestroy();
    }
}