using DG.Tweening;
using EPOOutline;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Outlinable), typeof(NetworkObject))]
public abstract class InteractableBase : NetworkBehaviour, IInteractable
{
    public abstract string InteractionText { get; }
    public abstract bool CanInteract(GameObject interactor);
    public abstract void Interact(GameObject interactor);

    // 조준 중인 플레이어 상태(예: 선택한 아이템)에 따라 안내 문구를 다르게 보여주고 싶을 때 오버라이드한다.
    // 기본은 정적인 InteractionText를 그대로 반환한다.
    public virtual string GetInteractionText(GameObject interactor) => InteractionText;

    // 안내 문구 옆에 키 힌트(" : [E]")를 보여줄지 결정한다. 눌러도 아무 동작이 없는 안내성 문구일 때 false로 오버라이드한다.
    public virtual bool ShowInteractionKeyHint(GameObject interactor) => true;

    // 채취·투입처럼 버튼을 길게 눌러야 완료되는 상호작용이면 true로 바꾼다.
    public virtual bool RequiresHoldInteraction(GameObject interactor) => false;

    // 길게 누르는 상호작용에 필요한 시간. PlayerInteraction의 기존 hold UI를 그대로 사용한다.
    public virtual float HoldInteractionDuration => 1.2f;

    // 화면 중심 조준 판정 반경에 곱해지는 배율. 기본은 1(PlayerInteraction의 공통 반경 그대로 사용).
    public virtual float AimRadiusMultiplier => 1f;

    public Vector3 InteractionPosition
    {
        get
        {
            bool hasBounds = false;
            Bounds combinedBounds = default;

            foreach (Collider interactionCollider in _interactionColliders)
            {
                if (interactionCollider == null ||
                    !interactionCollider.enabled ||
                    !interactionCollider.gameObject.activeInHierarchy ||
                    !interactionCollider.transform.IsChildOf(transform) ||
                    interactionCollider.isTrigger)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    combinedBounds = interactionCollider.bounds;
                    hasBounds = true;
                    continue;
                }


                combinedBounds.Encapsulate(interactionCollider.bounds);
            }

            return hasBounds ? combinedBounds.center : transform.position;
        }
    }

    [Header("외곽선 설정")]
    [SerializeField, Min(0f)] private float _outlineFadeDuration = 0.15f;   // 외곽선 페이드 지속 시간
    private Outlinable _outlinable;
    private Collider[] _interactionColliders;

    private Tween _outlineTween;
    private Color _outlineColor;    // 외곽선 색상 저장
    private Color _frontOutlineColor;   // 전면 외곽선 색상 저장
    private Color _backOutlineColor;    // 후면 외곽선 색상 저장
    private float _outlineVisibility;   // 외곽선 투명도

    protected virtual void Awake()
    {
        _interactionColliders = GetComponentsInChildren<Collider>();

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

    // 서버에서 상호작용 요청을 검증할 때 쓴다. 플레이어의 상호작용 콜라이더와 이 오브젝트의 콜라이더가 실제로 겹치는지 확인한다.
    protected bool IsOverlappingInteractionCollider(SphereCollider interactionCollider)
    {
        Collider[] itemColliders = GetComponentsInChildren<Collider>();

        foreach (Collider itemCollider in itemColliders)
        {
            if (!itemCollider.enabled || itemCollider == interactionCollider)
            {
                continue;
            }

            // Physics.ComputePenetration을 사용하여 상호작용 콜라이더와 대상 콜라이더가 겹치는지 확인
            if (Physics.ComputePenetration(interactionCollider, interactionCollider.transform.position, interactionCollider.transform.rotation,
                    itemCollider, itemCollider.transform.position, itemCollider.transform.rotation, out _, out _))
            {
                return true;
            }
        }

        return false;
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
