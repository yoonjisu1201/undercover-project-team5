using Unity.Netcode;

// 로컬 플레이어의 검거도구 사용(ArrestTool, 마우스 좌클릭) 홀드 상태를 서버에 동기화한다.
// ArrestChaseManager가 이 값을 읽어 게이지 축적 조건(홀드 중인 인원 수)에 반영한다.
public class PlayerArrestInput : NetworkBehaviour
{
    private readonly NetworkVariable<bool> _isHoldingArrestKey =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool IsHoldingArrestKey => _isHoldingArrestKey.Value;

    private CustomInputActions _actions;

    private void Awake()
    {
        _actions = new CustomInputActions();
    }

    private void OnEnable()
    {
        _actions ??= new CustomInputActions();
        _actions.Enable();
    }

    private void OnDisable()
    {
        _actions?.Disable();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            _actions.Disable();
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        // 메뉴/UI가 떠 있는 동안(GameplayUiMode.IsActive)은 홀드로 치지 않는다. (PlayerInteraction의 입력 차단 방식과 동일)
        _isHoldingArrestKey.Value = !GameplayUiMode.IsActive && _actions.Player.ArrestTool.IsPressed();
    }
}
