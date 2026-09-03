using Unity.Netcode;
using UnityEngine;

// 로컬 플레이어의 검거도구 사용(ArrestTool, 마우스 좌클릭) 홀드 상태를 서버에 동기화한다.
// ArrestChaseManager가 이 값을 읽어 게이지 축적 조건(홀드 중인 인원 수)에 반영한다.
public class PlayerArrestInput : NetworkBehaviour
{
    private static readonly int IsUsingArrestToolHash = Animator.StringToHash("IsUsingArrestTool");

    private readonly NetworkVariable<bool> _isHoldingArrestKey =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool IsHoldingArrestKey => _isHoldingArrestKey.Value;

    // 팔이 다 올라와 도구가 화면에 나온 뒤에야 손에 든 것들을 내린다.
    // 좌클릭 즉시 내리면 총과 손전등이 사라진 빈손이 올라간다.
    public bool IsToolVisualShown => _handToolVisual != null && _handToolVisual.activeSelf;

    // 손에 든 검거도구 시각 오브젝트. 기본 비활성으로 미리 배치해두고, 도구 장착 + 좌클릭 홀드 중일 때만 켠다.
    // 네트워크로 새로 스폰하지 않고 로컬에서 SetActive만 하므로, 모든 클라이언트(자기 자신 포함)에 미리 배치돼 있어야 한다.
    [SerializeField] private GameObject _handToolVisual;

    // 좌클릭과 동시에 도구를 켜면 총이 손에서 중앙으로 올라오기도 전에 이펙트가 먼저 터진다.
    // 팔이 올라오는 동안 기다렸다가 켠다. 뷰모델 손이 중앙으로 이동하는 시간과 맞춰 둔다.
    [Tooltip("좌클릭 후 도구가 켜지기까지 기다리는 시간(초). 팔이 올라오는 시간")]
    [SerializeField] private float _toolRaiseDelay = 0.22f;

    // 올리는 도중에 손을 떼면 켜지 않고 취소해야 한다.
    private Coroutine _raiseRoutine;

    private CustomInputActions _actions;
    private PlayerInventory _inventory;
    private PlayerHealth _health;
    private Animator _animator;

    private void Awake()
    {
        _actions = new CustomInputActions();
        _inventory = GetComponent<PlayerInventory>();
        _health = GetComponent<PlayerHealth>();
        _animator = GetComponent<Animator>();
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

        // 부착 여부와 마찬가지로, 늦게 들어온 클라이언트도 스폰 시점에 현재 값을 한 번 반영받는다.
        _isHoldingArrestKey.OnValueChanged += HandleHoldingChanged;
        ApplyVisual(_isHoldingArrestKey.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isHoldingArrestKey.OnValueChanged -= HandleHoldingChanged;
    }

    private void Update()
    {
        if (!IsOwner) return;

        // HP가 0이 되어 쓰러진 동안에는 홀드를 인정하지 않고 손에 든 도구도 내린다.
        // (AlienShotgunInput의 다운 처리와 같은 방식)
        if (_health != null && _health.IsDowned)
        {
            _isHoldingArrestKey.Value = false;
            return;
        }

        // 메뉴/UI가 떠 있는 동안(GameplayUiMode.IsActive)은 홀드로 치지 않는다. (PlayerInteraction의 입력 차단 방식과 동일)
        _isHoldingArrestKey.Value = !GameplayUiMode.IsActive && IsToolSelected() && _actions.Player.ArrestTool.IsPressed();
    }

    // 검거도구를 선택하지 않은 상태에서 좌클릭해도 홀드로 인정되지 않고, 손에도 표시되지 않는다.
    private bool IsToolSelected()
    {
        return _inventory != null
            && _inventory.TryGetSelectedItemId(out ItemType itemId)
            && itemId == ArrestChaseManager.CaptureToolItemId;
    }

    private void HandleHoldingChanged(bool previousValue, bool currentValue)
    {
        ApplyVisual(currentValue);
    }

    private void ApplyVisual(bool isHolding)
    {
        _animator?.SetBool(IsUsingArrestToolHash, isHolding);

        if (_handToolVisual == null)
        {
            return;
        }

        if (_raiseRoutine != null)
        {
            StopCoroutine(_raiseRoutine);
            _raiseRoutine = null;
        }

        // 내릴 때는 기다릴 이유가 없다. 바로 끈다.
        if (!isHolding)
        {
            _handToolVisual.SetActive(false);
            return;
        }

        // 지연은 내 1인칭에서 손이 올라오는 연출을 기다리기 위한 것이다.
        // 다른 클라이언트에서는 조준 자세가 즉시 잡히므로, 여기서 늦추면 총만 뒤늦게 나타나
        // 자세가 한 번 바뀐 뒤 총이 텔레포트한 것처럼 보인다.
        if (!IsOwner)
        {
            _handToolVisual.SetActive(true);
            return;
        }

        _raiseRoutine = StartCoroutine(ShowToolAfterRaise());
    }

    private System.Collections.IEnumerator ShowToolAfterRaise()
    {
        yield return new WaitForSeconds(_toolRaiseDelay);

        _handToolVisual.SetActive(true);
        _raiseRoutine = null;
    }
}
