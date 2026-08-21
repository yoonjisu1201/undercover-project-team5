using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 기계에 들어온 배터리 하나. 누가 넣었는지와 지금 어디에 있는지를 서버가 들고 있다.
// NetworkList에 담으려면 참조 타입 필드가 없어야 하고, INetworkSerializeByMemcpy를 붙여
// 직렬화 코드가 생성되도록 해야 한다. 이게 없으면 서버가 델타를 못 보내 클라이언트 목록이 계속 빈다.
public struct BreakerBatteryEntry : INetworkSerializeByMemcpy, IEquatable<BreakerBatteryEntry>
{
    public int Id;
    public int Watt;

    // 넣은 사람. 자기가 넣은 배터리만 옮기거나 뺄 수 있다.
    public ulong Owner;

    // -1이면 보관함, 0 이상이면 그 번호의 전원 슬롯이다.
    public int SlotIndex;

    public bool IsStored => SlotIndex < 0;

    public bool Equals(BreakerBatteryEntry other)
        => Id == other.Id && Watt == other.Watt && Owner == other.Owner && SlotIndex == other.SlotIndex;

    public override bool Equals(object obj) => obj is BreakerBatteryEntry other && Equals(other);

    public override int GetHashCode() => Id;
}

// 브레이커 배터리 미션에서 B(레버)·C(계기판)가 A와 공유해야 하는 전원·전력 상태를 서버 권한으로 관리한다.
// 배터리 보관함과 전원판 슬롯도 여기서 소유한다. 여러 명이 각자 가져온 배터리를 같은 기계에 모아 쓰기 때문에,
// 누가 무엇을 어디에 뒀는지를 모든 클라이언트가 같은 값으로 봐야 한다.
public sealed class BreakerCircuitState : NetworkBehaviour
{
    public const int SlotCount = 4;

    private readonly NetworkList<BreakerBatteryEntry> _batteries = new();
    private readonly List<BreakerBatteryEntry> _batteryCache = new();
    private int _nextBatteryId = 1;

    // A(배터리 패널)와 C(계기판)가 같은 시점에 결과 창을 띄우도록 연출 시간을 공유한다.
    // 계기판 바늘이 0에서 측정값까지 올라가는 시간이다.
    public const float MeasurementSweepSeconds = 0.9f;
    // 바늘이 멈춘 뒤 결과 창이 뜨기까지의 간격이다.
    public const float ResultDelaySeconds = 2f;

    private MissionInteractable _interactable;
    private FieldLampController _fieldLampController;

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

    // 전원·전력 값이 바뀔 때마다 알린다. (완료 여부는 MissionInteractable.IsCompleted를 직접 조회한다.)
    public event Action OnCircuitChanged;

    // A가 확인을 눌러 측정을 요청했을 때 알린다. C(계기판)들이 같은 시점에 같은 연출을 재생하기 위한 신호다.
    public event Action OnMeasurementRequested;

    private void Awake()
    {
        _interactable = GetComponent<MissionInteractable>();
    }

    public override void OnNetworkSpawn()
    {
        _batteries.OnListChanged += HandleBatteryListChanged;
        _powerOn.OnValueChanged += HandlePowerChanged;
        _currentWatt.OnValueChanged += HandleValueChanged;
        _targetWatt.OnValueChanged += HandleValueChanged;

        if (_interactable != null)
        {
            _interactable.IsCompletedChanged += HandleCompletionChanged;

            // 늦게 접속한 클라이언트는 이미 동기화된 초기값에 대한 변경 콜백을 받지 못하므로, 완료된 상태면 연출 없이 바로 켠다.
            if (_interactable.IsCompleted)
            {
                ApplyFieldLamps(true, withFade: false);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        _batteries.OnListChanged -= HandleBatteryListChanged;
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

    // 기계에 들어온 배터리 전부. 보관함과 슬롯을 모두 포함한다.
    // NetworkList는 IEnumerable<T>를 구현하지 않아 LINQ가 안 먹으므로 복사해 넘긴다.
    public IReadOnlyList<BreakerBatteryEntry> Batteries
    {
        get
        {
            _batteryCache.Clear();
            foreach (BreakerBatteryEntry entry in _batteries)
            {
                _batteryCache.Add(entry);
            }

            return _batteryCache;
        }
    }

    public int GetArrangedWatt()
    {
        int sum = 0;
        foreach (BreakerBatteryEntry entry in _batteries)
        {
            if (!entry.IsStored)
            {
                sum += entry.Watt;
            }
        }

        return sum;
    }

    // 인벤토리에서 실제로 꺼내진 배터리만 등록한다. 한 개당 정확히 한 번 호출해야 한다.
    public void ContributeBattery(int watt)
    {
        if (IsSpawned)
        {
            ContributeBatteryRpc(watt, NetworkManager.Singleton.LocalClientId);
        }
    }

    public void RequestPlaceBattery(int batteryId, int slotIndex)
    {
        if (IsSpawned)
        {
            PlaceBatteryRpc(batteryId, slotIndex, NetworkManager.Singleton.LocalClientId);
        }
    }

    public void RequestStoreBattery(int batteryId)
    {
        if (IsSpawned)
        {
            PlaceBatteryRpc(batteryId, -1, NetworkManager.Singleton.LocalClientId);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ContributeBatteryRpc(int watt, ulong requesterId)
    {
        if (watt <= 0)
        {
            return;
        }

        _batteries.Add(new BreakerBatteryEntry
        {
            Id = _nextBatteryId++,
            Watt = watt,
            Owner = requesterId,
            SlotIndex = -1
        });
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void PlaceBatteryRpc(int batteryId, int slotIndex, ulong requesterId)
    {
        if (_powerOn.Value || slotIndex >= SlotCount)
        {
            return;
        }

        int targetIndex = -1;
        for (int index = 0; index < _batteries.Count; index++)
        {
            if (_batteries[index].Id == batteryId)
            {
                targetIndex = index;
                break;
            }
        }

        if (targetIndex < 0)
        {
            return;
        }

        // 함께 쓰는 기계이므로 누가 넣은 배터리든 서로 옮길 수 있다.
        // Owner는 미션이 끝났을 때 누구 아이템이었는지 정산하는 용도로만 남긴다.
        BreakerBatteryEntry entry = _batteries[targetIndex];

        // 이미 다른 배터리가 차지한 칸에는 꽂지 않는다.
        if (slotIndex >= 0)
        {
            foreach (BreakerBatteryEntry other in _batteries)
            {
                if (other.Id != batteryId && other.SlotIndex == slotIndex)
                {
                    return;
                }
            }
        }

        entry.SlotIndex = slotIndex;
        _batteries[targetIndex] = entry;

        _arrangedWatt = GetArrangedWatt();
        if (_powerOn.Value)
        {
            _currentWatt.Value = _arrangedWatt;
        }
    }

    private void HandleBatteryListChanged(NetworkListEvent<BreakerBatteryEntry> changeEvent) => OnCircuitChanged?.Invoke();

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

        // 측정은 확인을 눌렀을 때만 한다. 전원이 들어와 있어야 전류가 흐르므로 그때만 판정한다.
        if (!_powerOn.Value || _targetWatt.Value == 0 || _currentWatt.Value != _targetWatt.Value)
        {
            return;
        }

        _interactable?.ServerCompleteFromGameplay(transform.position + transform.forward * 3f);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyMeasurementRpc()
    {
        OnMeasurementRequested?.Invoke();
    }

    // 전원이 들어오면 전류가 흘러 계기판에 값이 나타난다. 다만 여기서 완료 판정은 하지 않는다.
    // 합격 여부는 A가 '확인'을 눌러 측정을 요청했을 때만 따진다.
    private void HandlePowerChanged(bool previousValue, bool currentValue)
    {
        if (IsServer)
        {
            // 레버를 내리고 있는 동안에는 전류가 흐르지 않으므로 측정값을 0으로 되돌린다.
            _currentWatt.Value = currentValue ? _arrangedWatt : 0;
        }

        OnCircuitChanged?.Invoke();
    }

    private void HandleValueChanged(int previousValue, int currentValue) => OnCircuitChanged?.Invoke();


    // A가 배터리를 다 쓴 뒤 완료 여부를 반영해야 하는 쪽(예: BreakerBatteryMission)에 알린다.
    private void HandleCompletionChanged(bool completed)
    {
        ApplyFieldLamps(completed, withFade: true);

        OnCircuitChanged?.Invoke();
    }

    // 브레이커를 살리면 활성 구역의 가로등이 켜진다.
    // 가로등을 이벤트가 아니라 완료 상태의 함수로 두어, 이미 복제된 같은 값에서 서버와 클라이언트가 같은 결론을 내게 한다.
    // 라운드 리셋으로 완료가 풀리는 것이 곧 소등이므로 구역 변경 이벤트 순서에 의존하지 않는다.
    // 완료 상태 자체가 동기화되므로 추가 RPC 없이 각 피어가 로컬에서 연출만 재생한다.
    private void ApplyFieldLamps(bool completed, bool withFade)
    {
        // 브레이커는 런타임에 스폰되고 가로등은 씬에 배치돼 있어 인스펙터로 연결할 수 없다.
        if (_fieldLampController == null)
        {
            _fieldLampController = FindFirstObjectByType<FieldLampController>();
        }

        _fieldLampController?.SetLit(completed, withFade);
    }
}
