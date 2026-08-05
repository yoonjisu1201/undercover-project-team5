using System;
using Unity.Netcode;
using UnityEngine;

// 브레이커 배터리 미니게임에서 B(레버)·C(계기판)가 A와 공유해야 하는 전원·전력 상태를 서버 권한으로 관리한다.
// 배터리를 어느 슬롯에 놓았는지는 A만 보는 로컬 UI 상태로 남기고, 여기서는 공유가 필요한 값만 다룬다.
public sealed class BreakerCircuitState : NetworkBehaviour
{
    // A(배터리 패널)와 C(계기판)가 같은 시점에 결과 창을 띄우도록 연출 시간을 공유한다.
    // 계기판 바늘이 0에서 측정값까지 올라가는 시간이다.
    public const float MeasurementSweepSeconds = 0.9f;
    // 바늘이 멈춘 뒤 결과 창이 뜨기까지의 간격이다.
    public const float ResultDelaySeconds = 2f;

    private MiniGameInteractable _interactable;

    private readonly NetworkVariable<bool> _powerOn = new(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _currentWatt = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _targetWatt = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // A가 슬롯에 배치해 둔 합계다. 레버를 올려 전원이 들어오는 순간에만 측정값(_currentWatt)으로 반영한다.
    private int _arrangedWatt;

    public bool PowerOn => _powerOn.Value;
    public int CurrentWatt => _currentWatt.Value;
    public int TargetWatt => _targetWatt.Value;
    public bool IsCompleted => _interactable != null && _interactable.IsCompleted;

    // 전원·전력 값이 바뀔 때마다 알린다. (완료 여부는 MiniGameInteractable.IsCompleted를 직접 조회한다.)
    public event Action OnCircuitChanged;

    // A가 확인을 눌러 측정을 요청했을 때 알린다. C(계기판)들이 같은 시점에 같은 연출을 재생하기 위한 신호다.
    public event Action OnMeasurementRequested;

    private void Awake()
    {
        _interactable = GetComponent<MiniGameInteractable>();
    }

    public override void OnNetworkSpawn()
    {
        _powerOn.OnValueChanged += HandlePowerChanged;
        _currentWatt.OnValueChanged += HandleValueChanged;
        _targetWatt.OnValueChanged += HandleValueChanged;

        if (_interactable != null)
        {
            _interactable.IsCompletedChanged += HandleCompletionChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        _powerOn.OnValueChanged -= HandlePowerChanged;
        _currentWatt.OnValueChanged -= HandleValueChanged;
        _targetWatt.OnValueChanged -= HandleValueChanged;

        if (_interactable != null)
        {
            _interactable.IsCompletedChanged -= HandleCompletionChanged;
        }
    }

    // B가 레버를 누르는 동안 false(Off), 손을 떼는 순간 true(On)를 보낸다.
    public void SetPower(bool powerOn)
    {
        if (IsSpawned)
        {
            SetPowerRpc(powerOn);
        }
    }

    // A가 처음 배터리를 배치할 때 자신의 인벤토리 조합으로 계산한 목표 전력을 한 번만 등록한다.
    public void SubmitTargetWatt(int watt)
    {
        if (IsSpawned)
        {
            SetTargetWattRpc(watt);
        }
    }

    // A가 확인을 누른 시점을 모든 계기판에 전파한다.
    public void RequestMeasurement()
    {
        if (IsSpawned)
        {
            RequestMeasurementRpc();
        }
    }

    // A의 슬롯 구성이 바뀔 때마다 합계를 보고한다.
    public void ReportCurrentWatt(int watt)
    {
        if (IsSpawned)
        {
            ReportCurrentWattRpc(watt);
        }
    }

    // 이 기계는 서버 소유이지만 RPC를 호출하는 건 레버/배터리 패널을 조작하는 클라이언트들이다.
    // 기본값(소유자만 호출 가능)으로는 막히므로 Everyone으로 열어둔다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetPowerRpc(bool powerOn)
    {
        _powerOn.Value = powerOn;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetTargetWattRpc(int watt)
    {
        if (_targetWatt.Value != 0)
        {
            return;
        }

        _targetWatt.Value = watt;
    }

    // 클라이언트가 직접 다른 클라이언트로 보낼 수 없으므로, 서버를 거쳐 모든 계기판에 다시 뿌린다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestMeasurementRpc()
    {
        NotifyMeasurementRpc();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyMeasurementRpc()
    {
        OnMeasurementRequested?.Invoke();
    }

    // 레버를 내리고 있는 동안(전원 Off)에는 아직 측정하지 않고 배치만 기록해 둔다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ReportCurrentWattRpc(int watt)
    {
        _arrangedWatt = watt;

        // 전원이 들어와 있는 동안에는 배치를 바꿀 수 없으므로, 들어온 값은 그대로 측정값에 반영한다.
        if (_powerOn.Value)
        {
            _currentWatt.Value = watt;
        }
    }

    // 레버를 올려 전원이 들어온 순간에만 배치를 측정하고, 목표 전력과 일치하면 완료 처리한다.
    private void HandlePowerChanged(bool previousValue, bool currentValue)
    {
        if (IsServer)
        {
            // 레버를 내리고 있는 동안에는 전류가 흐르지 않으므로 측정값을 0으로 되돌린다.
            _currentWatt.Value = currentValue ? _arrangedWatt : 0;
        }

        OnCircuitChanged?.Invoke();

        if (!IsServer || !currentValue || _targetWatt.Value == 0 || _currentWatt.Value != _targetWatt.Value)
        {
            return;
        }

        _interactable?.ServerCompleteFromGameplay(transform.position + transform.forward * 3f);
    }

    private void HandleValueChanged(int previousValue, int currentValue) => OnCircuitChanged?.Invoke();


    // A가 배터리를 다 쓴 뒤 완료 여부를 반영해야 하는 쪽(예: BreakerBatteryMiniGame)에 알린다.
    private void HandleCompletionChanged(bool completed) => OnCircuitChanged?.Invoke();
}
