using System;
using Unity.Netcode;
using UnityEngine;

// 통신 주파수 동기화 미니게임에서 세 역할이 공유해야 하는 상태를 서버 권한으로 관리한다.
// P1(파형 화면)  : 안테나 방향을 돌려 신호가 가장 센 방향(=목표 방향)을 찾아 P2에게 알려준다.
// P2(안테나 운반): 안테나를 들고 그 방향으로 이동해 안테나 존에 들어간다.
// P3(주파수 다이얼): 안테나가 존에 들어온 뒤에야 다이얼을 돌려 목표 주파수를 맞출 수 있다.
public sealed class FrequencySyncState : NetworkBehaviour
{
    public const float MinFrequency = 100f;
    public const float MaxFrequency = 115f;
    // 다이얼을 0.05MHz 단위로 움직이므로 허용 오차도 한 칸으로 둔다.
    public const float FrequencyTolerance = 0.05f;
    // 안테나 방향이 목표에서 이만큼 벗어나면 신호가 완전히 사라진다. 값이 작을수록 P1이 방향을 더 정밀하게 찾아야 한다.
    public const float SignalBeamWidth = 90f;
    // 신호 세기가 이 값을 넘으면 P1 화면이 '거의 일치함'으로 바뀐다.
    private const float NearMatchSignal = 0.9f;

    // 기계에서 안테나 존까지의 거리다. 존은 목표 방향 쪽에 배치되므로 P1이 읽은 각도가 곧 P2의 이동 방향이 된다.
    [SerializeField] private float _zoneDistance = 12f;
    // 목표 방향에 맞춰 위치를 옮길 안테나 존이다.
    [SerializeField] private FrequencyAntennaZone _antennaZone;
    // 이 아이템을 들고 있는 플레이어가 P2다. 존 판정과 레이더 추적이 같은 기준을 쓰도록 여기서 한 번만 정한다.
    [SerializeField] private string _antennaItemId = "Antenna";
    // 안테나 소지자를 다시 찾아 좌표를 갱신하는 간격이다. 매 프레임 돌 필요는 없다.
    private const float TrackRefreshSeconds = 0.2f;

    private MiniGameInteractable _interactable;

    private readonly NetworkVariable<int> _targetBearing = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _antennaBearing = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _antennaPlaced = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _targetFrequency = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _currentFrequency = new(
        MinFrequency, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 목표 방향을 0도(레이더의 북쪽)로 놓았을 때, 안테나 소지자가 기계에서 어느 쪽에 서 있는지다.
    // 0에 가까울수록 목표 방향으로 제대로 가고 있다는 뜻이다.
    private readonly NetworkVariable<float> _holderRelativeBearing = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // 안테나 소지자에서 존까지 남은 거리(m)다. 소지자가 없으면 -1이다.
    private readonly NetworkVariable<float> _holderDistance = new(
        -1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float _nextTrackTime;

    public int TargetBearing => _targetBearing.Value;
    public int AntennaBearing => _antennaBearing.Value;
    public bool AntennaPlaced => _antennaPlaced.Value;
    public float TargetFrequency => _targetFrequency.Value;
    public float CurrentFrequency => _currentFrequency.Value;
    public bool IsCompleted => _interactable != null && _interactable.IsCompleted;
    public string AntennaItemId => _antennaItemId;

    // 목표 방향 기준 안테나 소지자의 상대 각도다. 레이더에서 목표는 항상 북쪽이고 이 각도만큼 소지자 표식이 돌아간다.
    public float HolderRelativeBearing => _holderRelativeBearing.Value;
    // 소지자에서 존까지 남은 거리(m). 소지자를 못 찾으면 -1이다.
    public float HolderDistance => _holderDistance.Value;
    public bool HasHolder => _holderDistance.Value >= 0f;

    // 안테나가 존에 들어오기 전에는 다이얼을 잠근다. 세 역할이 순서대로 맞물리게 하는 유일한 잠금이다.
    public bool DialUnlocked => _antennaPlaced.Value && !IsCompleted;

    // 목표 방향과 현재 안테나 방향의 각도 차다. P1이 방향을 찾는 기준이 된다.
    public float BearingError => Mathf.Abs(Mathf.DeltaAngle(_antennaBearing.Value, _targetBearing.Value));

    // 방향이 목표에 가까울수록 1에 가까워진다. 안테나가 존에 자리 잡으면 방향 탐색은 끝난 것으로 보고 최대치를 유지한다.
    public float SignalStrength01 => _antennaPlaced.Value
        ? 1f
        : Mathf.Clamp01(1f - BearingError / SignalBeamWidth);

    // 방향이 거의 맞았는지. P1 화면의 '동기화 상태' 문구에 쓴다.
    public bool BearingNearMatched => SignalStrength01 >= NearMatchSignal;

    // 상태가 바뀔 때마다 알린다. 세 화면 모두 이 이벤트만 구독해 다시 그린다.
    public event Action OnStateChanged;

    private void Awake()
    {
        _interactable = GetComponent<MiniGameInteractable>();
    }

    public override void OnNetworkSpawn()
    {
        _holderRelativeBearing.OnValueChanged += HandleTrackingChanged;
        _holderDistance.OnValueChanged += HandleTrackingChanged;
        _targetBearing.OnValueChanged += HandleIntChanged;
        _antennaBearing.OnValueChanged += HandleIntChanged;
        _antennaPlaced.OnValueChanged += HandleBoolChanged;
        _targetFrequency.OnValueChanged += HandleFloatChanged;
        _currentFrequency.OnValueChanged += HandleFloatChanged;

        if (_interactable != null)
        {
            _interactable.IsCompletedChanged += HandleBoolChanged;
            _interactable.ServerRoundReset += RandomizePuzzle;
        }

        if (IsServer && _targetFrequency.Value == 0f)
        {
            RandomizePuzzle();
        }

        ApplyZonePlacement();
    }

    public override void OnNetworkDespawn()
    {
        _holderRelativeBearing.OnValueChanged -= HandleTrackingChanged;
        _holderDistance.OnValueChanged -= HandleTrackingChanged;
        _targetBearing.OnValueChanged -= HandleIntChanged;
        _antennaBearing.OnValueChanged -= HandleIntChanged;
        _antennaPlaced.OnValueChanged -= HandleBoolChanged;
        _targetFrequency.OnValueChanged -= HandleFloatChanged;
        _currentFrequency.OnValueChanged -= HandleFloatChanged;

        if (_interactable != null)
        {
            _interactable.IsCompletedChanged -= HandleBoolChanged;
            _interactable.ServerRoundReset -= RandomizePuzzle;
        }
    }

    // 목표 방향·목표 주파수를 서버가 정한다. 라운드가 새로 시작될 때마다 다시 뽑으므로 정답을 외울 수 없다.
    private void RandomizePuzzle()
    {
        if (!IsServer)
        {
            return;
        }

        // 지난 라운드에 안테나를 설치해 둔 상태로 시작하지 않도록 진행 단계도 되돌린다.
        _antennaPlaced.Value = false;
        _targetBearing.Value = UnityEngine.Random.Range(0, 360);

        // 시작 방향이 우연히 정답에 붙어 있으면 P1이 찾을 게 없으므로, 신호가 잡히지 않는 각도에서 시작한다.
        int offset = UnityEngine.Random.Range((int)SignalBeamWidth, 360 - (int)SignalBeamWidth);
        _antennaBearing.Value = (_targetBearing.Value + offset) % 360;

        // 다이얼 눈금과 어긋나 절대 맞출 수 없는 목표가 나오지 않도록 0.05MHz 배수로 맞춘다.
        int steps = Mathf.RoundToInt((MaxFrequency - MinFrequency) / FrequencyTolerance);
        _targetFrequency.Value = MinFrequency + UnityEngine.Random.Range(1, steps) * FrequencyTolerance;
        _currentFrequency.Value = MinFrequency;
    }

    // 존을 목표 방향 쪽으로 옮긴다. 이걸로 'P1이 읽은 각도 = P2가 가야 하는 방향'이 실제로 성립한다.
    private void ApplyZonePlacement()
    {
        if (_antennaZone == null || _targetFrequency.Value == 0f)
        {
            return;
        }

        Quaternion bearing = Quaternion.Euler(0f, _targetBearing.Value, 0f);
        _antennaZone.transform.position = transform.position + bearing * Vector3.forward * _zoneDistance;
    }

    // 안테나 소지자의 좌표는 서버만 알고 있으므로, 서버가 주기적으로 재서 공유 값에 담는다.
    private void Update()
    {
        if (!IsServer || Time.time < _nextTrackTime)
        {
            return;
        }

        _nextTrackTime = Time.time + TrackRefreshSeconds;
        TrackHolderOnServer();
    }

    private void TrackHolderOnServer()
    {
        Transform holder = FindAntennaHolder();
        if (holder == null)
        {
            _holderDistance.Value = -1f;
            return;
        }

        // 기계에서 소지자를 향하는 방위각을 목표 방향 기준으로 환산한다.
        // 레이더에서 목표는 항상 북쪽이므로, 이 값이 0이면 소지자가 목표 방향 선상에 있다는 뜻이다.
        Vector3 toHolder = holder.position - transform.position;
        toHolder.y = 0f;
        float holderBearing = Mathf.Atan2(toHolder.x, toHolder.z) * Mathf.Rad2Deg;
        _holderRelativeBearing.Value = Mathf.DeltaAngle(_targetBearing.Value, holderBearing);

        // 남은 거리는 존 중심까지의 수평 거리로 잰다.
        Vector3 zoneCenter = _antennaZone != null ? _antennaZone.transform.position : transform.position;
        Vector3 toZone = zoneCenter - holder.position;
        toZone.y = 0f;
        _holderDistance.Value = toZone.magnitude;
    }

    // 인벤토리 어느 칸에든 안테나가 있는 플레이어를 소지자로 본다.
    private Transform FindAntennaHolder()
    {
        foreach (Player player in Player.ActiveInstances)
        {
            PlayerInventory inventory = player != null ? player.PlayerInventory : null;
            if (inventory == null)
            {
                continue;
            }

            foreach (InventorySlot slot in inventory.Slots)
            {
                if (slot != null && slot.ItemId == _antennaItemId)
                {
                    return player.transform;
                }
            }
        }

        return null;
    }

    // P1이 안테나 방향을 좌우로 돌린다.
    public void SubmitAntennaBearing(int bearing)
    {
        if (IsSpawned)
        {
            SetAntennaBearingRpc(((bearing % 360) + 360) % 360);
        }
    }

    // P3가 다이얼을 돌린 값을 보고한다.
    public void SubmitFrequency(float frequency)
    {
        if (IsSpawned)
        {
            SetFrequencyRpc(Mathf.Clamp(frequency, MinFrequency, MaxFrequency));
        }
    }

    // 안테나 존이 서버에서 진입·이탈을 판정해 알린다.
    public void SetAntennaPlacedOnServer(bool placed)
    {
        if (IsServer && _antennaPlaced.Value != placed)
        {
            _antennaPlaced.Value = placed;
        }
    }

    // 이 기계는 서버 소유지만 RPC를 호출하는 쪽은 각 화면을 조작하는 클라이언트다. Breaker와 같은 이유로 Everyone으로 열어둔다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetAntennaBearingRpc(int bearing)
    {
        // 안테나가 자리 잡은 뒤에는 방향 탐색이 끝났으므로 더 돌릴 수 없다.
        if (!_antennaPlaced.Value)
        {
            _antennaBearing.Value = bearing;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetFrequencyRpc(float frequency)
    {
        if (!DialUnlocked)
        {
            return;
        }

        _currentFrequency.Value = frequency;
        TryCompleteOnServer();
    }

    // 안테나가 존에 있고 주파수가 허용 오차 안에 들어오는 순간 바로 완료한다.
    private void TryCompleteOnServer()
    {
        if (!_antennaPlaced.Value || _targetFrequency.Value == 0f)
        {
            return;
        }

        if (Mathf.Abs(_currentFrequency.Value - _targetFrequency.Value) > FrequencyTolerance)
        {
            return;
        }

        _interactable?.ServerCompleteFromGameplay(transform.position + transform.forward * 3f);
    }

    private void HandleIntChanged(int previousValue, int currentValue)
    {
        ApplyZonePlacement();
        OnStateChanged?.Invoke();
    }

    private void HandleFloatChanged(float previousValue, float currentValue)
    {
        ApplyZonePlacement();
        OnStateChanged?.Invoke();
    }

    // 소지자 추적 값은 자주 바뀌므로 존 위치 재계산 없이 화면 갱신만 알린다.
    private void HandleTrackingChanged(float previousValue, float currentValue) => OnStateChanged?.Invoke();

    private void HandleBoolChanged(bool previousValue, bool currentValue) => OnStateChanged?.Invoke();

    private void HandleBoolChanged(bool currentValue) => OnStateChanged?.Invoke();
}
