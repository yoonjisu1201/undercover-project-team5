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

    [Tooltip("레이저를 한 번에 유지할 수 있는 시간(초). 게이지가 가득 찼을 때의 길이다")]
    [SerializeField, Min(0.1f)] private float _attackSeconds = 7f;

    // 쏘기를 멈추면 잠깐 기다렸다가 다시 차오른다.
    [Tooltip("쏘기를 멈춘 뒤 게이지가 다시 차기 시작할 때까지의 시간(초)")]
    [SerializeField, Min(0f)] private float _regenDelay = 1f;

    [Tooltip("게이지가 차는 속도(초당). 클수록 빨리 회복한다")]
    [SerializeField, Min(0.01f)] private float _regenPerSecond = 3f;

    // 끝에 닿기 전부터 빨갛게 경고하되, 경고 구간에서도 계속 쏠 수 있어야 한다.
    // 빨개지는 순간 멈추면 남은 구간을 아예 못 쓴다.
    [Tooltip("이 비율 아래로 내려가면 게이지가 빨갛게 바뀐다. 그래도 계속 쏠 수 있다")]
    [SerializeField, Range(0f, 0.5f)] private float _redZoneRatio = 0.25f;

    private float _attackRemaining;
    private float _regenResumeTime;

    // 다 쓰고 난 뒤에는 누르고 있던 손을 한 번 떼야 다시 나간다.
    private bool _waitingForRelease;
    private bool _didShowLaserThisHold;

    // 올리는 도중에 손을 떼면 켜지 않고 취소해야 한다.
    private Coroutine _raiseRoutine;

    private CustomInputActions _actions;
    private PlayerInventory _inventory;
    private PlayerHealth _health;
    private Animator _animator;
    private ArrestToolGaugeUI _gaugeUi;

    private void Awake()
    {
        _actions = new CustomInputActions();
        _inventory = GetComponent<PlayerInventory>();
        _health = GetComponent<PlayerHealth>();
        _animator = GetComponent<Animator>();
        _attackRemaining = _attackSeconds;
    }

    private void OnEnable()
    {
        _actions ??= new CustomInputActions();
        _actions.Enable();

        // 꺼져 있는 동안 코루틴이 끊겼을 수 있다. 지금 상태로 다시 맞춘다.
        if (IsSpawned)
        {
            ApplyVisual(_isHoldingArrestKey.Value);
        }
    }

    private void OnDisable()
    {
        _actions?.Disable();

        // 비활성화되면 유니티가 코루틴을 죽인다. 핸들만 남겨 두면 다시 켜졌을 때
        // 도구가 영영 안 나타난 채로 남는다.
        if (_raiseRoutine != null)
        {
            StopCoroutine(_raiseRoutine);
            _raiseRoutine = null;
        }
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
            UpdateRegen(false);
            UpdateGaugeUi(false);
            return;
        }

        bool isToolSelected = IsToolSelected();

        // 메뉴/UI가 떠 있는 동안(GameplayUiMode.IsActive)은 홀드로 치지 않는다. (PlayerInteraction의 입력 차단 방식과 동일)
        bool canUse = !GameplayUiMode.IsActive && isToolSelected;
        bool pressed = _actions.Player.ArrestTool.IsPressed();
        bool wasHolding = _isHoldingArrestKey.Value;
        float ratio = _attackSeconds > 0f ? _attackRemaining / _attackSeconds : 1f;

        // 쏘던 중이면 빨간 구간에 들어서도 끊지 않는다. 남은 구간을 못 쓰게 되면
        // 경고가 아니라 그냥 더 짧은 게이지일 뿐이다.
        // 대신 손을 뗀 뒤에는 흰색으로 돌아올 때까지 다시 못 쏜다.
        bool canStart = ratio > _redZoneRatio;
        bool wants = canUse && pressed && _attackRemaining > 0f && (wasHolding || canStart);

        // 다 쓰고 나면 손을 뗐다 다시 눌러야 한다. 안 그러면 꾹 누르고 있는 것만으로
        // 차오르는 족족 다시 나가서 기다린 의미가 없어진다.
        if (_waitingForRelease)
        {
            if (pressed)
            {
                wants = false;
            }
            else
            {
                _waitingForRelease = false;
            }
        }

        if (!wasHolding && wants)
        {
            _didShowLaserThisHold = false;
        }

        _isHoldingArrestKey.Value = wants;

        UpdateRegen(wants && _didShowLaserThisHold);
        UpdateGaugeUi(canUse);
    }

    // 쏘는 동안은 닳고, 멈추면 기다렸다가 차오른다. 끝까지 다 쓰면 더 오래 기다린다.
    // 다른 아이템을 들어도 회복은 계속 돈다 - 손에 없다고 멈추면 바꿔 들 때마다 손해다.
    private void UpdateRegen(bool firing)
    {
        if (firing)
        {
            _attackRemaining = Mathf.Max(0f, _attackRemaining - Time.deltaTime);

            // 쏘는 동안 매 프레임 뒤로 민다. 시작할 때 한 번만 재면 계속 쏘는 사이에
            // 대기가 끝나 버려서, 손을 떼자마자 곧바로 차오른다.
            _regenResumeTime = Time.time + _regenDelay;

            // 다 써도 벌칙은 없다. 다른 때와 똑같이 기다렸다가 차오른다.
            if (_attackRemaining <= 0f)
            {
                _waitingForRelease = true;
                _didShowLaserThisHold = false;
                _isHoldingArrestKey.Value = false;
            }

            return;
        }

        if (_attackRemaining >= _attackSeconds || Time.time < _regenResumeTime)
        {
            return;
        }

        _attackRemaining = Mathf.Min(_attackSeconds, _attackRemaining + _regenPerSecond * Time.deltaTime);
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

    // 손에 들고 있고 가득 차지 않았을 때만 보인다. 다 차면 알아서 사라진다.
    // 다른 아이템으로 바꾸면 감추기만 하고 회복은 계속 돈다.
    private void UpdateGaugeUi(bool canUse)
    {
        ArrestToolGaugeUI gauge = ResolveGaugeUi();
        if (gauge == null)
        {
            return;
        }

        if (!canUse || _attackRemaining >= _attackSeconds)
        {
            gauge.SetVisible(false);
            return;
        }

        float ratio = _attackSeconds > 0f ? Mathf.Clamp01(_attackRemaining / _attackSeconds) : 1f;

        // 경고 구간에서만 빨갛다. 차오르다 임계점을 넘으면 그 자리에서 흰색으로 돌아온다.
        gauge.SetDanger(ratio <= _redZoneRatio);
        gauge.SetGauge(ratio);
    }

    private ArrestToolGaugeUI ResolveGaugeUi()
    {
        if (_gaugeUi == null)
        {
            _gaugeUi = ArrestToolGaugeUI.Resolve();
        }

        return _gaugeUi;
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

        if (!IsOwner || GameplayUiMode.IsActive || !IsToolSelected() || !_actions.Player.ArrestTool.IsPressed())
        {
            _raiseRoutine = null;
            yield break;
        }

        _handToolVisual.SetActive(true);
        _didShowLaserThisHold = true;
        _raiseRoutine = null;
    }
}
