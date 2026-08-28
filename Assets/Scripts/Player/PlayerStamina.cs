using System;
using Unity.Netcode;
using UnityEngine;

// 플레이어의 스태미나를 관리한다. 달리는 동안 소모되고, 달리지 않으면 시간이 지나며 자연 회복된다.
public class PlayerStamina : NetworkBehaviour
{
    [Header("스태미나 설정 (임시 기본값, 추후 밸런싱 이슈로 조정)")]
    [SerializeField] private float _maxStamina = 50f;
    [SerializeField] private float _drainPerSecond = 20f;
    [SerializeField] private float _regenPerSecond = 5f;

    [Header("회복 대기")]
    [Tooltip("달리기를 멈춘 뒤 회복이 시작되기까지의 시간. 떼자마자 차오르면 달리기와 걷기를 "
        + "잘게 번갈아 쓰는 것이 이득이 된다. 달리기는 막지 않고 회복만 늦춘다.")]
    [SerializeField, Min(0f)] private float _regenDelay = 1f;

    [Tooltip("빨간 구간까지 쓰고 멈춘 경우의 대기 시간. 이때만은 회복과 함께 달리기도 막힌다.")]
    [SerializeField, Min(0f)] private float _redZoneRegenDelay = 3f;

    [Tooltip("빨간 구간으로 볼 남은 비율. HUD 게이지가 붉어지는 지점과 같게 둔다. "
        + "0 으로 두면 완전히 바닥났을 때만 패널티가 붙는다.")]
    [SerializeField, Range(0f, 0.5f)] private float _redZoneRatio = 0.2f;

    private const float EmptyThreshold = 0.01f;

    private readonly NetworkVariable<float> _currentStamina =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 잠김이 풀리는 서버 시각. 달리기 판정은 오너의 PlayerMoveSample 이 클라이언트에서 내리고
    // HUD 도 남은 시간을 알아야 하므로 상태를 내려보낸다. 매 프레임 남은 시간을 보내는 대신
    // 끝나는 시각만 한 번 보내고 각자 세게 한다.
    private readonly NetworkVariable<double> _redZoneLockEndTime =
        new(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 서버 전용 상태. 오너의 StartRunning/StopRunning 호출로만 갱신된다.
    private bool _isSprinting;

    // 회복이 다시 시작되는 서버 시각. 달리는 동안 계속 뒤로 밀리므로 멈춘 시점부터 온전히 세어진다.
    private double _regenResumeTime;

    // 직전 프레임에 달리고 있었는지. 잠김 종료 시각을 멈추는 순간에 한 번만 내려보내기 위해 둔다.
    private bool _wasSprinting;

    // 이번 대기가 빨간 구간까지 쓴 대가인지. 평소 대기와 대가를 구분해야, 짧게 쉬프트를 떼는
    // 것마다 달리기가 끊기는 일 없이 빨간 구간에서만 사용을 막을 수 있다.
    private bool _isRedZoneStop;

    // 외부에서 변경 감지 구독
    public event Action<float, float> StaminaChanged;

    public float MaxStamina => _maxStamina;
    public float CurrentStamina => _currentStamina.Value;

    // 달리기가 끊기는 기준. 소모는 부동소수 누적 탓에 정확히 0에서 멈추지 않으므로
    // 0과 비교하지 말고 이 판정을 쓴다.
    public bool IsEmpty => _currentStamina.Value <= EmptyThreshold;

    // 보스 감지가 "지금 달리는 중인가"를 알아야 한다(달리기가 가장 큰 소음원).
    // 서버에서만 갱신되는 상태라 클라이언트에서는 항상 false 다.
    public bool IsSprinting => _isSprinting;

    // 빨간 구간까지 쓴 대가를 치르는 중인지. 이 동안에는 회복도 달리기도 막힌다.
    // 달리기 판정은 오너가 클라이언트에서 내리고 HUD 도 이 상태를 읽으므로 공개한다.
    public bool IsRedZonePenalized
    {
        get
        {
            if (!IsSpawned || NetworkManager == null) return false;

            return _redZoneLockEndTime.Value > NetworkManager.ServerTime.Time;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _currentStamina.Value = _maxStamina;
        }

        _currentStamina.OnValueChanged += HandleStaminaChanged;
    }

    public override void OnNetworkDespawn()
    {
        _currentStamina.OnValueChanged -= HandleStaminaChanged;
    }

    private void FixedUpdate()
    {
        if (!IsServer || !IsSpawned) return;

        double now = NetworkManager.ServerTime.Time;

        if (_isSprinting)
        {
            _currentStamina.Value =
                Mathf.Max(0f, _currentStamina.Value - _drainPerSecond * Time.fixedDeltaTime);

            // 대기는 달리는 동안 매 프레임 뒤로 밀어둔다. 진입한 순간부터 세면 계속 달리는
            // 사이에 대기가 끝나버려서, 멈추자마자 곧바로 차오른다.
            _isRedZoneStop = IsInRedZone;
            _regenResumeTime = now + (_isRedZoneStop ? _redZoneRegenDelay : _regenDelay);

            if (_currentStamina.Value <= EmptyThreshold)
            {
                _isSprinting = false;
            }

            // 달리는 중에는 잠그지 않는다. 잠그면 빨간 구간에 들어서는 순간 달리기가 끊겨서
            // 남은 구간을 아예 쓸 수 없게 된다. 대가는 멈춘 뒤에 치른다.
            _redZoneLockEndTime.Value = 0d;
            _wasSprinting = true;
            return;
        }

        // 멈추는 순간에만 잠김 종료 시각을 알린다. 평소 대기(_regenDelay)는 달리기를 막지 않으므로
        // 알릴 것이 없다.
        if (_wasSprinting)
        {
            _wasSprinting = false;
            _redZoneLockEndTime.Value = _isRedZoneStop ? _regenResumeTime : 0d;
        }

        if (now < _regenResumeTime) return;

        _currentStamina.Value =
            Mathf.Min(_maxStamina, _currentStamina.Value + _regenPerSecond * Time.fixedDeltaTime);
    }

    // 남은 양이 빨간 구간인지. 완전히 바닥난 경우는 비율 기준과 별개로 항상 포함한다.
    private bool IsInRedZone =>
        _maxStamina <= 0f
        || _currentStamina.Value <= EmptyThreshold
        || _currentStamina.Value / _maxStamina <= _redZoneRatio;

    // 오너의 PlayerMoveSample이 달리기를 시작했을 때 호출하는 공개 진입점.
    public void StartRunning()
    {
        if (!IsOwner)
        {
            Debug.LogError("[PlayerStamina] StartRunning은 오너만 호출할 수 있습니다.");
            return;
        }

        if (CurrentStamina <= EmptyThreshold)
        {
            StopRunningServerRpc();
            return;
        }

        StartRunningServerRpc();
    }

    // 오너의 PlayerMoveSample이 달리기를 멈췄을 때 호출하는 공개 진입점.
    public void StopRunning()
    {
        if (!IsOwner)
        {
            Debug.LogError("[PlayerStamina] StopRunning은 오너만 호출할 수 있습니다.");
            return;
        }

        StopRunningServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void StartRunningServerRpc()
    {
        _isSprinting = _currentStamina.Value > EmptyThreshold && !IsRedZonePenalized;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void StopRunningServerRpc()
    {
        _isSprinting = false;
    }

    // 라운드 시작/재시작 시 스태미나를 최대치로 되돌린다. 서버에서만 호출 가능하다.
    public void ResetForNewRound()
    {
        if (!IsServer) return;

        _currentStamina.Value = _maxStamina;
        _isSprinting = false;
        _wasSprinting = false;
        _isRedZoneStop = false;
        _regenResumeTime = 0d;
        _redZoneLockEndTime.Value = 0d;
    }

    // 소생 직후에 부르면 스태미나가 바닥에서 시작한다. 쓰러졌다 일어난 사람이 곧바로
    // 전력으로 달아날 수 있으면 소생의 무게가 사라진다. 서버에서만 호출 가능하다.
    public void Deplete()
    {
        if (!IsServer) return;

        _currentStamina.Value = 0f;
        _isSprinting = false;

        // 소생 직후에도 바닥은 바닥이다. 대가를 빼면 일어선 자리에서 곧바로 차오르기 시작한다.
        _wasSprinting = false;
        _isRedZoneStop = true;
        _regenResumeTime = NetworkManager.ServerTime.Time + _redZoneRegenDelay;
        _redZoneLockEndTime.Value = _regenResumeTime;
    }

    private void HandleStaminaChanged(float previousValue, float newValue)
    {
        StaminaChanged?.Invoke(previousValue, newValue);
    }
}
