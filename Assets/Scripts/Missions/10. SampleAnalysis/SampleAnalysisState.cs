using System;
using Unity.Netcode;
using UnityEngine;

// 오염 샘플 분석 미션에서 세 역할이 공유해야 하는 상태를 서버 권한으로 관리한다.
// P1(관찰 모니터) : 수치를 볼 수 없다. 반응 단계만 보고 어느 쪽으로 움직여야 하는지 P2, P3에게 말해준다.
// P2(온도 조절)   : 수치는 보이지만 정답 범위를 모른다. P1이 말하는 대로 온도를 올리거나 내린다.
// P3(농도 조절)   : 같은 방식으로 농도를 맞춘다.
// 두 값이 동시에 정답 범위에 들어간 상태를 4초 버텨야 끝난다. 값이 계속 밀리므로 한 번 맞추고 손을 뗄 수는 없다.
public sealed class SampleAnalysisState : NetworkBehaviour
{
    // 역할이 비어 있음을 나타내는 값이다. 클라이언트 ID 0번이 호스트라서 0을 빈 값으로 쓸 수 없다.
    public const ulong EmptyClientId = ulong.MaxValue;

    // 두 값이 움직일 수 있는 전체 범위다. 조작과 노이즈 모두 이 안으로 묶는다.
    private const float MinTemperature = 20f;
    private const float MaxTemperature = 60f;
    private const float MinConcentration = 0f;
    private const float MaxConcentration = 100f;

    // 정답 범위를 벗어났을 때 게이지가 줄어드는 속도 배수다.
    // 1이면 살짝 흔들린 것만으로 쌓은 걸 그대로 잃어서 복구가 사실상 불가능하다.
    private const float StableDecayMultiplier = 0.5f;

    // 두 값의 오차를 하나의 반응 단계로 합칠 때 나누는 폭이다. 조작 범위가 서로 달라 같은 비중으로 맞춘다.
    private const float TemperatureErrorScale = 8f;
    private const float ConcentrationErrorScale = 18f;
    // 합친 오차가 이 값 이하일 때의 단계다. 둘 다 넘으면 세포 붕괴로 본다.
    private const float DetectedErrorThreshold = 0.45f;
    private const float UnstableErrorThreshold = 1.25f;

    [Header("정답 범위")]
    // P2, P3가 맞춰야 하는 구간이다. 플레이어에게는 보여주지 않는다.
    [SerializeField] private Vector2 _targetTemperatureRange = new(27f, 33f);
    [SerializeField] private Vector2 _targetConcentrationRange = new(30f, 40f);
    [SerializeField] private float _requiredStableSeconds = 4f; // 정답 범위 안에서 4초간 머물러야 미션이 완료됨

    [Header("초기값")]
    // 정답 범위 밖에서 출발해야 세 역할이 서로 말을 맞출 이유가 생긴다.
    [SerializeField] private float _initialTemperature = 38f;
    [SerializeField] private float _initialConcentration = 24f;

    [Header("조작/노이즈")]
    // 버튼 한 번에 움직이는 양이다.
    [SerializeField] private float _temperatureStep = 2f;
    [SerializeField] private float _concentrationStep = 2f;
    // 값이 저절로 밀리는 속도다. 맞춰 놓은 뒤에도 계속 붙어 있게 만든다.
    // 가만히 두면 온도는 6초, 농도는 12초쯤 뒤에 정답 범위를 벗어난다.
    // 버티는 4초 안에 한 번씩만 손보면 되는 정도라, 계속 신경은 쓰되 허둥대지는 않는다.
    [SerializeField] private float _temperatureDriftPerSecond = 0.5f;
    [SerializeField] private float _concentrationDriftPerSecond = -0.4f;

    private MissionInteractable _interactable;
    // 라운드 초기화가 한 번은 돌아야 한다. 초기값이 들어가기 전에 노이즈가 돌면 엉뚱한 값에서 출발한다.
    private bool _initialized;

    private readonly NetworkVariable<float> _temperature = new(
        40f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _concentration = new(
        20f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // 정답 범위 안에서 머문 시간이다. 필요 시간을 채우면 미션이 완료된다.
    private readonly NetworkVariable<float> _stableSeconds = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 역할별 담당자다. 한 자리를 두 명이 잡지 못하게 서버가 점유를 관리한다.
    private readonly NetworkVariable<ulong> _observerClientId = new(
EmptyClientId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> _temperatureClientId = new(
        EmptyClientId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> _concentrationClientId = new(
        EmptyClientId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public float Temperature => _temperature.Value;
    public float Concentration => _concentration.Value;
    public float StableSeconds => _stableSeconds.Value;
    public float RequiredStableSeconds => _requiredStableSeconds;
    public bool IsCompleted => _interactable != null && _interactable.IsCompleted;

    // 두 값이 모두 정답 범위 안인지. 이 상태에서만 안정화 시간이 쌓인다.
    public bool IsStable =>
        IsInRange(Temperature, _targetTemperatureRange)
        && IsInRange(Concentration, _targetConcentrationRange);

    public SampleReactionLevel ReactionLevel => CalculateReactionLevel();

    // P1이 P2, P3에게 전달할 방향이다. 올려야 하면 1, 내려야 하면 -1, 맞으면 0이다.
    public int TemperatureDirection => GetAdjustmentDirection(Temperature, _targetTemperatureRange);
    public int ConcentrationDirection => GetAdjustmentDirection(Concentration, _targetConcentrationRange);

    // 상태가 바뀔 때마다 알린다. 세 화면 모두 이 이벤트만 구독해 다시 그린다.
    public event Action OnStateChanged;

    private void Awake()
    {
        _interactable = GetComponent<MissionInteractable>();
    }

    public override void OnNetworkSpawn()
    {
        _temperature.OnValueChanged += HandleFloatChanged;
        _concentration.OnValueChanged += HandleFloatChanged;
        _stableSeconds.OnValueChanged += HandleFloatChanged;
        _observerClientId.OnValueChanged += HandleClientIdChanged;
        _temperatureClientId.OnValueChanged += HandleClientIdChanged;
        _concentrationClientId.OnValueChanged += HandleClientIdChanged;

        if (_interactable != null)
        {
            _interactable.IsCompletedChanged += HandleCompletedChanged;
            _interactable.ServerRoundReset += ResetServerState;
        }

        if (IsServer)
        {
            ResetServerState();
        }
    }

    public override void OnNetworkDespawn()
    {
        _temperature.OnValueChanged -= HandleFloatChanged;
        _concentration.OnValueChanged -= HandleFloatChanged;
        _stableSeconds.OnValueChanged -= HandleFloatChanged;
        _observerClientId.OnValueChanged -= HandleClientIdChanged;
        _temperatureClientId.OnValueChanged -= HandleClientIdChanged;
        _concentrationClientId.OnValueChanged -= HandleClientIdChanged;

        if (_interactable != null)
        {
            _interactable.IsCompletedChanged -= HandleCompletedChanged;
            _interactable.ServerRoundReset -= ResetServerState;
        }
    }

    // 노이즈와 안정화 시간은 서버만 계산한다. 샘플을 넣기 전에는 장치가 돌지 않는다.
    private void Update()
    {
        if (!IsServer || !_initialized || IsCompleted)
        {
            return;
        }

        if (_interactable != null && !_interactable.IsRequiredItemInserted)
        {
            return;
        }

        DriftValuesOnServer();
        UpdateStableSecondsOnServer();

        if (_stableSeconds.Value >= _requiredStableSeconds)
        {
            _interactable?.ServerCompleteFromGameplay(transform.position + transform.forward * 2f);
        }
    }

    private void DriftValuesOnServer()  // 값이 저절로 밀리는 노이즈를 서버에서 계산한다. 정답 범위 안에 있으면 그대로 유지된다.
    {
        _temperature.Value = Mathf.Clamp(
            _temperature.Value + _temperatureDriftPerSecond * Time.deltaTime,
            MinTemperature, MaxTemperature);
        _concentration.Value = Mathf.Clamp(
            _concentration.Value + _concentrationDriftPerSecond * Time.deltaTime,
            MinConcentration, MaxConcentration);
    }

    // 범위 안이면 시간을 쌓고, 벗어나면 천천히 깎는다. 바로 0으로 만들면 복구할 여지가 없다.
    private void UpdateStableSecondsOnServer()
    {
        if (IsStable)
        {
            _stableSeconds.Value = Mathf.Min(_requiredStableSeconds, _stableSeconds.Value + Time.deltaTime);
            return;
        }

        _stableSeconds.Value = Mathf.Max(0f, _stableSeconds.Value - Time.deltaTime * StableDecayMultiplier);
    }

    // P2가 온도 버튼을 누른 값을 보고한다.
    public void AdjustTemperature(float delta)
    {
        if (IsSpawned)
        {
            AdjustTemperatureRpc(delta);
        }
    }

    // P3가 농도 버튼을 누른 값을 보고한다.
    public void AdjustConcentration(float delta)
    {
        if (IsSpawned)
        {
            AdjustConcentrationRpc(delta);
        }
    }

    public void RequestRole(SampleAnalysisRole role)
    {
        if (IsSpawned)
        {
            RequestRoleRpc(role);
        }
    }

    public void ReleaseRole(SampleAnalysisRole role)
    {
        if (IsSpawned)
        {
            ReleaseRoleRpc(role);
        }
    }

    public ulong GetAssignedClientId(SampleAnalysisRole role)
    {
        switch (role)
        {
            case SampleAnalysisRole.Observer: return _observerClientId.Value;
            case SampleAnalysisRole.Temperature: return _temperatureClientId.Value;
            default: return _concentrationClientId.Value;
        }
    }

    // 자리가 비어 있거나 이미 내가 앉아 있으면 고를 수 있다.
    public bool IsRoleAvailable(SampleAnalysisRole role, ulong clientId)
    {
        ulong assigned = GetAssignedClientId(role);
        return assigned == EmptyClientId || assigned == clientId;
    }

    // 이 기계는 서버 소유지만 RPC를 호출하는 쪽은 각 화면을 조작하는 클라이언트다. 주파수 미션과 같은 이유로 Everyone으로 열어둔다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestRoleRpc(SampleAnalysisRole role, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        NetworkVariable<ulong> slot = GetRoleSlot(role);

        // 이미 다른 사람이 앉아 있으면 무시한다. 화면에서 잠가두지만 동시에 누르면 여기까지 올 수 있다.
        if (slot.Value == EmptyClientId || slot.Value == clientId)
        {
            slot.Value = clientId;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ReleaseRoleRpc(SampleAnalysisRole role, RpcParams rpcParams = default)
    {
        // 남의 자리를 빼앗지 못하도록 지금 앉아 있는 사람의 요청만 받는다.
        NetworkVariable<ulong> slot = GetRoleSlot(role);
        if (slot.Value == rpcParams.Receive.SenderClientId)
        {
            slot.Value = EmptyClientId;
        }
    }

    // 조작은 그 역할을 맡은 사람만 할 수 있다. 그러지 않으면 옆에서 아무나 값을 흔들 수 있다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void AdjustTemperatureRpc(float delta, RpcParams rpcParams = default)
    {
        if (IsCompleted || _temperatureClientId.Value != rpcParams.Receive.SenderClientId)
        {
            return;
        }

        _temperature.Value = Mathf.Clamp(
            _temperature.Value + delta * _temperatureStep, MinTemperature, MaxTemperature);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void AdjustConcentrationRpc(float delta, RpcParams rpcParams = default)
    {
        if (IsCompleted || _concentrationClientId.Value != rpcParams.Receive.SenderClientId)
        {
            return;
        }

        _concentration.Value = Mathf.Clamp(
            _concentration.Value + delta * _concentrationStep, MinConcentration, MaxConcentration);
    }

    // 라운드가 시작될 때 수치와 진행도, 역할 점유를 처음 상태로 되돌린다.
    private void ResetServerState()
    {
        if (!IsServer)
        {
            return;
        }

        _temperature.Value = _initialTemperature;
        _concentration.Value = _initialConcentration;
        _stableSeconds.Value = 0f;
        _observerClientId.Value = EmptyClientId;
        _temperatureClientId.Value = EmptyClientId;
        _concentrationClientId.Value = EmptyClientId;
        _initialized = true;
    }

    private NetworkVariable<ulong> GetRoleSlot(SampleAnalysisRole role)
    {
        switch (role)
        {
            case SampleAnalysisRole.Observer: return _observerClientId;
            case SampleAnalysisRole.Temperature: return _temperatureClientId;
            default: return _concentrationClientId;
        }
    }

    // 두 값이 정답 범위에서 얼마나 벗어났는지를 단계 하나로 압축한다.
    // P1은 수치를 볼 수 없으므로 이 단계만으로 얼마나 가까워졌는지 판단해야 한다.
    private SampleReactionLevel CalculateReactionLevel()
    {
        float error = DistanceFromRange(Temperature, _targetTemperatureRange) / TemperatureErrorScale
            + DistanceFromRange(Concentration, _targetConcentrationRange) / ConcentrationErrorScale;

        if (error <= 0f)
        {
            return SampleReactionLevel.Stable;
        }

        if (error <= DetectedErrorThreshold)
        {
            return SampleReactionLevel.Detected;
        }

        return error <= UnstableErrorThreshold
            ? SampleReactionLevel.Unstable
            : SampleReactionLevel.Collapse;
    }

    private void HandleFloatChanged(float previousValue, float currentValue) => OnStateChanged?.Invoke();

    private void HandleClientIdChanged(ulong previousValue, ulong currentValue) => OnStateChanged?.Invoke();

    private void HandleCompletedChanged(bool currentValue) => OnStateChanged?.Invoke();

    private static bool IsInRange(float value, Vector2 range) => value >= range.x && value <= range.y;

    // 범위보다 낮으면 1(올려야 함), 높으면 -1(내려야 함), 범위 안이면 0이다.
    private static int GetAdjustmentDirection(float value, Vector2 range)
    {
        if (value < range.x)
        {
            return 1;
        }

        return value > range.y ? -1 : 0;
    }

    // 범위 밖에 있을 때 가장 가까운 경계까지의 거리다.
    private static float DistanceFromRange(float value, Vector2 range)
    {
        if (value < range.x)
        {
            return range.x - value;
        }

        return value > range.y ? value - range.y : 0f;
    }
}

// 두 값이 정답 범위에 얼마나 가까운지 나타내는 반응 단계다. P1 화면의 색과 파형이 이 값으로 바뀐다.
public enum SampleReactionLevel
{
    Stable,
    Detected,
    Unstable,
    Collapse
}

// 샘플 분석 장치에서 플레이어가 맡을 수 있는 세 역할이다.
public enum SampleAnalysisRole
{
    Observer,
    Temperature,
    Concentration
}
