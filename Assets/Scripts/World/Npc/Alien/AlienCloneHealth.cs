using System;
using Unity.Netcode;
using UnityEngine;

// 외계인 복제체의 체력을 관리한다. 플레이어와 달리 다운 상태로 대기하지 않고,
// 체력이 0이 되면 처치된 것으로 간주한다. 실제 디스폰은 AlienCloneManager가 Died 이벤트를 구독해 처리한다.
public class AlienCloneHealth : NetworkBehaviour, IDamageable
{
    [Header("HP 설정 (임시 기본값, 추후 밸런싱 이슈로 조정)")]
    [SerializeField] private float _maxHp = 50f;

    private readonly NetworkVariable<float> _currentHp =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _isDowned =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 외부에서 변경 감지 구독
    public event Action<float, float> HpChanged;
    public event Action Died;

    public float MaxHp => _maxHp;
    public float CurrentHp => _currentHp.Value;
    public bool IsDowned => _isDowned.Value;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _currentHp.Value = _maxHp;
        }

        _currentHp.OnValueChanged += HandleHpChanged;
    }

    public override void OnNetworkDespawn()
    {
        _currentHp.OnValueChanged -= HandleHpChanged;
    }

    // 외부(외계생체 제압기 등)에서 데미지를 입힐 때 호출하는 공개 진입점. 서버에서만 호출 가능하다.
    public void TakeDamage(float amount)
    {
        if (!IsServer)
        {
            Debug.LogError("[AlienCloneHealth] TakeDamage는 서버에서만 호출할 수 있습니다.");
            return;
        }

        if (_isDowned.Value || amount <= 0f) return;

        _currentHp.Value = Mathf.Max(0f, _currentHp.Value - amount);
        Debug.Log($"[AlienCloneHealth] {amount} 데미지 적용, 남은 HP: {_currentHp.Value}/{_maxHp}");

        if (_currentHp.Value <= 0f)
        {
            _isDowned.Value = true;
            Died?.Invoke();
        }
    }

    private void HandleHpChanged(float previousValue, float newValue)
    {
        HpChanged?.Invoke(previousValue, newValue);
    }
}
