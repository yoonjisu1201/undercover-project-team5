using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;

// B 역할의 레버. 상호작용 키를 누르고 있는 동안만 회로를 Off로 유지하고, 손을 떼는 순간 즉시 On으로 돌아간다.
// 배터리 UI(MiniGameInteractable 기반)와는 별개의 상호작용이라 A가 패널을 열어둔 상태에서도 동시에 조작할 수 있다.
public sealed class BreakerLeverInteractable : InteractableBase
{
    // 정확히 180도를 목표로 잡으면 절대 각도 보간의 최단 경로가 애매해져(안테포달) 한 바퀴를 더 도는 문제가 생긴다.
    // 그래서 육안으로는 구분 안 되는 179.9도를 써서 그 특이점을 피한다.
    // 절대 목표 각도로 보간하므로, 다 내려가기 전에 손을 떼는 등 애니메이션이 도중에 끊겨도
    // 다음 목표를 향해 이어서 자연스럽게 움직인다(상대 회전 방식과 달리 어긋나지 않는다).
    private static readonly Vector3 RestRotation = Vector3.zero;
    private static readonly Vector3 PulledRotation = new(-179.9f, 0f, 0f);
    private const float ArmTweenDuration = 1.5f;

    [SerializeField] private string _interactionText = "레버 누르고 있기";

    private BreakerCircuitState _circuitState;
    private CustomInputActions _actions;
    private Transform _armPivot;
    private Tween _armTween;
    // 이 필드는 상호작용을 시작한 이 클라이언트에서만 true가 된다 (다른 플레이어의 화면에는 영향 없음).
    private bool _isHoldingLocally;

    public override string InteractionText => _interactionText;
    public override bool CanInteract(GameObject interactor) => _circuitState != null;

    protected override void Awake()
    {
        base.Awake();
        // 레버는 배터리 UI를 여는 MiniGameInteractable과 별개의 오브젝트이므로, 공유 상태는 부모(MG_04 루트)에서 찾는다.
        _circuitState = GetComponentInParent<BreakerCircuitState>();
        _actions = new CustomInputActions();
        _armPivot = transform.Find("LeverArmPivotGroup");
    }

    private void OnEnable()
    {
        _actions ??= new CustomInputActions();
        _actions.Enable();
        if (_circuitState != null)
        {
            // 재컴파일 등으로 OnEnable이 중복 호출돼도 구독이 여러 번 쌓이지 않도록 먼저 해제한다.
            _circuitState.OnCircuitChanged -= HandleCircuitChanged;
            _circuitState.OnCircuitChanged += HandleCircuitChanged;
        }

        // 모든 클라이언트가 현재 전원 상태에 맞는 레버 각도로 시작하도록 즉시 반영한다.
        SnapToCurrentState();
    }

    private void OnDisable()
    {
        _actions?.Disable();
        ReleaseIfHolding();
        if (_circuitState != null)
        {
            _circuitState.OnCircuitChanged -= HandleCircuitChanged;
        }
    }

    public override void Interact(GameObject interactor)
    {
        if (_isHoldingLocally || _circuitState == null)
        {
            return;
        }

        _isHoldingLocally = true;
        _circuitState.SetPower(false);
    }

    // 상호작용 시스템은 누르는 순간(WasPressedThisFrame)만 알려주므로, 키를 계속 누르고 있는지는 여기서 직접 폴링한다.
    private void Update()
    {
        if (_isHoldingLocally && _actions != null && !_actions.Player.Interact.IsPressed())
        {
            ReleaseIfHolding();
        }
    }

    private void ReleaseIfHolding()
    {
        if (!_isHoldingLocally)
        {
            return;
        }

        _isHoldingLocally = false;
        _circuitState?.SetPower(true);
    }

    // 전원 상태를 보는 모든 클라이언트에서 레버 팔이 실제로 오르내리도록 애니메이션한다.
    private void HandleCircuitChanged()
    {
        if (_armPivot == null || _circuitState == null)
        {
            return;
        }

        Vector3 target = _circuitState.PowerOn ? RestRotation : PulledRotation;
        _armTween?.Kill();
        _armTween = _armPivot.DOLocalRotate(target, ArmTweenDuration).SetEase(Ease.OutQuad);
    }

    // 애니메이션 없이 현재 전원 상태에 맞는 각도로 즉시 맞춘다 (최초 활성화 시 사용).
    private void SnapToCurrentState()
    {
        if (_armPivot == null || _circuitState == null)
        {
            return;
        }

        _armTween?.Kill();
        _armPivot.localRotation = Quaternion.Euler(_circuitState.PowerOn ? RestRotation : PulledRotation);
    }

    public override void OnDestroy()
    {
        _armTween?.Kill();
        ReleaseIfHolding();
        base.OnDestroy();
    }
}
