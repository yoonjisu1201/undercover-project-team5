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

    private const float EmptyThreshold = 0.01f;

    private readonly NetworkVariable<float> _currentStamina =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 서버 전용 상태. 오너의 StartRunning/StopRunning 호출로만 갱신된다.
    private bool _isSprinting;

    // 외부에서 변경 감지 구독
    public event Action<float, float> StaminaChanged;

    public float MaxStamina => _maxStamina;
    public float CurrentStamina => _currentStamina.Value;

    // 달리기가 끊기는 기준. 소모는 부동소수 누적 탓에 정확히 0에서 멈추지 않으므로
    // 0과 비교하지 말고 이 판정을 쓴다.
    public bool IsEmpty => _currentStamina.Value <= EmptyThreshold;

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

        float delta = (_isSprinting ? -_drainPerSecond : _regenPerSecond) * Time.fixedDeltaTime;
        _currentStamina.Value = Mathf.Clamp(_currentStamina.Value + delta, 0f, _maxStamina);

        if (_isSprinting && _currentStamina.Value <= EmptyThreshold)
        {
            _isSprinting = false;
        }
    }

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
        _isSprinting = _currentStamina.Value > EmptyThreshold;
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
    }

    private void HandleStaminaChanged(float previousValue, float newValue)
    {
        StaminaChanged?.Invoke(previousValue, newValue);
    }
}
