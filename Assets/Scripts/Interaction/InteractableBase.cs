using DG.Tweening;
using EPOOutline;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;

// NetworkObject를 여기서 요구하지 않는다. NetworkBehaviour는 부모 체인에 NetworkObject가 있으면 되는데,
// 같은 오브젝트에 요구하면 다른 네트워크 프리팹의 자식으로 붙일 때 Unity가 자식에 NetworkObject를 자동 생성한다.
// 그렇게 만들어진 중첩 NetworkObject는 동적 스폰에서 네트워크로 스폰되지 않아 조용히 망가진다.
[RequireComponent(typeof(Outlinable))]
public abstract class InteractableBase : NetworkBehaviour, IInteractable
{
    [Header("상호작용 안내 문구")]
    [Tooltip("조준했을 때 보여줄 문구. 문구가 상황에 따라 달라지는 대상만 InteractionText 를 오버라이드한다.")]
    // 이름을 그대로 두면 안 된다. MissionInteractable·BreakerLeverInteractable 이 같은 이름의
    // string 필드를 이미 갖고 있어서 직렬화 이름이 겹친다.
    [SerializeField] private LocalizedString _localizedInteractionText;

    // 문구를 인스펙터에서 지정하게 두는 이유는, 파생 클래스마다 같은 코드를 반복하지 않으려는 것이다.
    // 상황에 따라 문구가 갈리는 대상(예: 방장인지에 따라 다른 준비 버튼)만 이 프로퍼티를 오버라이드한다.
    public virtual string InteractionText => _localizedInteractionText.GetLocalizedString();

    // 프리팹이 Assets/Imported 처럼 gitignore 대상인 대상은 인스펙터 지정이 팀원에게 전파되지 않는다.
    // 그런 경우만 이 헬퍼로 키를 코드에 남기고 InteractionText 를 오버라이드한다.
    protected static string LocalizeInteractionText(string localizationKey, params object[] arguments)
    {
        var localized = new LocalizedString(InteractionTextTable, localizationKey);
        return arguments == null || arguments.Length == 0
            ? localized.GetLocalizedString()
            : localized.GetLocalizedString(arguments);
    }

    private const string InteractionTextTable = "Language Table";
    public abstract bool CanInteract(GameObject interactor);
    public abstract void Interact(GameObject interactor);

    // 조준 중인 플레이어 상태(예: 선택한 아이템)에 따라 안내 문구를 다르게 보여주고 싶을 때 오버라이드한다.
    // 기본은 정적인 InteractionText를 그대로 반환한다.
    public virtual string GetInteractionText(GameObject interactor) => InteractionText;

    // 안내 문구 옆에 키 힌트(" : [E]")를 보여줄지 결정한다. 눌러도 아무 동작이 없는 안내성 문구일 때 false로 오버라이드한다.
    public virtual bool ShowInteractionKeyHint(GameObject interactor) => true;

    // 상호작용을 완료하기까지 눌러야 하는 시간(초). 0이면 누르는 즉시 완료된다.
    public virtual float InteractHoldThreshold => 0f;

    // 화면 중심 조준 판정 반경에 곱해지는 배율. 기본은 1(PlayerInteraction의 공통 반경 그대로 사용).
    public virtual float AimRadiusMultiplier => 1f;

    // 트리거를 벗어난 뒤에도 이 거리까지는 상호작용 판정을 유지한다. 0이면 트리거 판정만 쓴다.
    // 계속 걸어 다니는 NPC처럼, 판정이 끊기면 홀드 게이지가 처음부터 다시 차는 대상에만 준다.
    public virtual float ExtendedInteractionRange => 0f;

    // 상호작용 조준점 계산에서 뺄 콜라이더. 아이템 낙하 방지용처럼 상호작용 면이 아닌 콜라이더가
    // 바운드에 끼면 조준점이 실제 오브젝트 밖으로 밀려난다.
    [Header("조준 설정")]
    [SerializeField] private Collider[] _aimIgnoredColliders;

    // 콜라이더가 실제 외형과 어긋나는 대상(쓰러진 플레이어 등)은 이 값을 재정의해 조준점을 옮긴다.
    public virtual Vector3 InteractionPosition
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
                    interactionCollider.isTrigger ||
                    IsAimIgnored(interactionCollider))
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

    private bool IsAimIgnored(Collider target)
    {
        if (_aimIgnoredColliders == null)
        {
            return false;
        }

        foreach (Collider ignored in _aimIgnoredColliders)
        {
            if (ignored == target)
            {
                return true;
            }
        }

        return false;
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
