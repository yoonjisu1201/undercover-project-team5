using UnityEngine;
using UnityEngine.Localization;

// 하나의 월드 버튼을 호스트에게는 게임 시작, 참가자에게는 준비·취소 기능으로 제공한다.
// 역할과 준비 상태는 WaitingRoomUI에서 조회해 기존 Canvas UI와 같은 규칙과 상태를 공유한다.
public sealed class ReadyStartWorldButton : WaitingRoomButtonBase
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // 이 버튼은 조준한 사람의 역할과 준비 상태에 따라 문구가 갈리므로, 공통 필드 하나로는 안 된다.
    // 그래서 InteractableBase 의 기본 구현 대신 아래 세 문구를 직접 고른다.
    [Header("상호작용 안내 문구 (역할·상태별)")]
    [SerializeField] private LocalizedString _gameStart;
    [SerializeField] private LocalizedString _ready;
    [SerializeField] private LocalizedString _readyCancel;

    [Header("준비·시작 표시")]
    [SerializeField] private Renderer _buttonRenderer;
    [SerializeField] private Renderer _statusLampRenderer;

    [SerializeField] private Color _participantNotReadyBaseColor = new Color(0.13f, 0.13f, 0.13f, 1f);

    [ColorUsage(false, true)]
    [SerializeField] private Color _participantNotReadyEmissionColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    [SerializeField] private Color _readyBaseColor = new Color(0.02f, 0.2f, 0.05f, 1f);

    [ColorUsage(false, true)]
    [SerializeField] private Color _readyEmissionColor = new Color(0.1f, 0.75f, 0.2f, 1f);

    [SerializeField] private Color _startUnavailableBaseColor = new Color(0.2f, 0.02f, 0.02f, 1f);

    [ColorUsage(false, true)]
    [SerializeField] private Color _startUnavailableEmissionColor = new Color(0.75f, 0.1f, 0.08f, 1f);

    [SerializeField] private Color _startingBaseColor = new Color(0.01f, 0.06f, 0.2f, 1f);

    [ColorUsage(false, true)]
    [SerializeField] private Color _startingEmissionColor = new Color(0.04f, 0.45f, 2.05f, 1f);

    private MaterialPropertyBlock _materialProperties;
    private bool _hasStarted;

    public override string InteractionText =>
        (RoomUI.IsHost ? _gameStart : _ready).GetLocalizedString();

    public override bool CanInteract(GameObject interactor) => RoomUI.CanUseReadyStart;

    public override string GetInteractionText(GameObject interactor)
    {
        if (RoomUI.IsHost)
        {
            return _gameStart.GetLocalizedString();
        }

        // 참가자는 같은 버튼으로 준비 상태를 토글하므로 현재 상태에 맞는 다음 동작을 안내한다.
        return (RoomUI.IsReady ? _readyCancel : _ready).GetLocalizedString();
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
        if (RoomUI.IsHost)
        {
            _hasStarted = true;
            RefreshVisual();
        }

        RoomUI.InteractReadyStart();
    }

    private void RefreshVisual()
    {
        Color baseColor;
        Color emissionColor;

        if (RoomUI.IsHost)
        {
            if (_hasStarted)
            {
                baseColor = _startingBaseColor;
                emissionColor = _startingEmissionColor;
            }
            else if (RoomUI.CanUseReadyStart)
            {
                baseColor = _readyBaseColor;
                emissionColor = _readyEmissionColor;
            }
            else
            {
                baseColor = _startUnavailableBaseColor;
                emissionColor = _startUnavailableEmissionColor;
            }
        }
        else if (RoomUI.IsReady)
        {
            baseColor = _readyBaseColor;
            emissionColor = _readyEmissionColor;
        }
        else
        {
            baseColor = _participantNotReadyBaseColor;
            emissionColor = _participantNotReadyEmissionColor;
        }

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
