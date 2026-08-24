using UnityEngine;

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

        return RoomUI.IsReady ? "준비 취소" : "준비";
    }

    protected override void Awake()
    {
        base.Awake();

        _materialProperties = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        RoomUI.ReadyStartStateChanged += RefreshVisual;
        RefreshVisual();
    }

    protected override void OnDisable()
    {
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

        _materialProperties.Clear();
        _buttonRenderer.GetPropertyBlock(_materialProperties);
        _materialProperties.SetColor(BaseColorId, baseColor);
        _materialProperties.SetColor(EmissionColorId, emissionColor);
        _buttonRenderer.SetPropertyBlock(_materialProperties);

        // 램프 표면색은 유지하고 발광색만 버튼과 맞춘다.
        _materialProperties.Clear();
        _statusLampRenderer.GetPropertyBlock(_materialProperties);
        _materialProperties.SetColor(EmissionColorId, emissionColor);
        _statusLampRenderer.SetPropertyBlock(_materialProperties);
    }
}
