using DG.Tweening;
using EPOOutline;
using UnityEngine;

public class PickupItem : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemData _itemData;

    [Header("외곽선 설정")]
    [SerializeField] private Outlinable _outlinable;
    [SerializeField, Min(0f)] private float _outlineFadeDuration = 0.15f;   // 외곽선 페이드 지속 시간

    private Tween _outlineTween;
    private Color _outlineColor;    // 외곽선 색상 저장
    private Color _frontOutlineColor;   // 전면 외곽선 색상 저장
    private Color _backOutlineColor;    // 후면 외곽선 색상 저장
    private float _outlineVisibility;   // 외곽선 투명도
    private float _interactionBlockedUntil;

    public string InteractionText => _itemData != null ? $"{_itemData.DisplayName} 줍기" : "앉기";
    public bool CanInteract => Time.time >= _interactionBlockedUntil;   // 상호작용 가능 여부

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

    public void BlockInteraction(float duration)    // duration초 동안 상호작용 차단
    {
        _interactionBlockedUntil = Time.time + Mathf.Max(0f, duration);
        SetOutline(false);
    }

    //--- 기존 코드 ---//
    public void Interact(GameObject interactor)
    {
        if (!CanInteract || _itemData == null)
        {
            return;
        }

        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();

        if (inventory == null)
        {
            return;
        }

        if (inventory.TryAddItem(_itemData.ItemId))
        {
            Destroy(gameObject);
        }
    }

    //--- 외곽선 색, Alpha 값 적용 ---//
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

    private void OnDestroy()
    {
        _outlineTween?.Kill();
    }
}
