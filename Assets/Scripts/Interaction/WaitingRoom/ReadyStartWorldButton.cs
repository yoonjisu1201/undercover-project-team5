using UnityEngine;

// 하나의 월드 버튼을 호스트에게는 게임 시작, 참가자에게는 준비·취소 기능으로 제공한다.
// 역할과 준비 상태는 WaitingRoomUI에서 조회해 기존 Canvas UI와 같은 규칙과 상태를 공유한다.
public sealed class ReadyStartWorldButton : WaitingRoomButtonBase
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Header("준비·시작 표시")]
    [SerializeField] private Renderer _buttonRenderer;
    [SerializeField] private Renderer _statusLampRenderer;

    [SerializeField] private Color _inactiveBaseColor = new Color(0.025f, 0.16f, 0.7f, 1f);

    [ColorUsage(false, true)]
    [SerializeField] private Color _inactiveEmissionColor = new Color(0.05f, 1.2f, 7f, 1f);

    [SerializeField] private Color _activeBaseColor = new Color(0.7f, 0.025f, 0.015f, 1f);

    [ColorUsage(false, true)]
    [SerializeField] private Color _activeEmissionColor = new Color(7f, 0.08f, 0.03f, 1f);

    private MaterialPropertyBlock _materialProperties;

    public override string InteractionText => RoomUI.IsHost ? "게임 시작" : "준비";

    public override bool CanInteract(GameObject interactor) => RoomUI.CanUseReadyStart;

    public override string GetInteractionText(GameObject interactor)
    {
        if (RoomUI.IsHost)
        {
            return "게임 시작";
        }

        // 참가자는 같은 버튼으로 준비 상태를 토글하므로 현재 상태에 맞는 다음 동작을 안내한다.
        return RoomUI.IsReady ? "준비 취소" : "준비";
    }

    protected override void Awake()
    {
        base.Awake();

        _materialProperties = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        // 다른 플레이어의 준비 상태도 시작 가능 여부를 바꾸므로 이벤트로 갱신하고, 활성화 직후 현재 상태도 1회 반영한다.
        RoomUI.ReadyStartStateChanged += RefreshVisual;
        RefreshVisual();
    }

    protected override void OnDisable()
    {
        // 비활성화된 버튼이 상태 변경을 계속 받지 않도록 구독을 해제한 뒤 공통 눌림 상태를 복구한다.
        if (RoomUI != null)
        {
            RoomUI.ReadyStartStateChanged -= RefreshVisual;
        }

        base.OnDisable();
    }

    protected override void ExecuteButtonAction()
    {
        RoomUI.InteractReadyStart();
    }

    private void RefreshVisual()
    {
        bool isActive = RoomUI.IsReadyStartActive;

        Color baseColor = isActive ? _activeBaseColor : _inactiveBaseColor;
        Color emissionColor = isActive ? _activeEmissionColor : _inactiveEmissionColor;

        // 공유 Material을 복제하지 않고 이 버튼 인스턴스의 색만 바꾸며, 기존 PropertyBlock 값은 보존한다.
        _buttonRenderer.GetPropertyBlock(_materialProperties);
        _materialProperties.SetColor(BaseColorId, baseColor);
        _materialProperties.SetColor(EmissionColorId, emissionColor);
        _buttonRenderer.SetPropertyBlock(_materialProperties);

        // 램프 고유 표면색은 유지하고 발광색만 버튼과 맞춰 하나의 준비·시작 상태로 보이게 한다.
        _statusLampRenderer.GetPropertyBlock(_materialProperties);
        _materialProperties.SetColor(EmissionColorId, emissionColor);
        _statusLampRenderer.SetPropertyBlock(_materialProperties);
    }
}
