using System;
using Unity.Netcode;
using UnityEngine;

// 플레이어의 HP를 관리한다. 시간이 지나면 자연 감소하고, 0이 되면 죽지 않고 다운(쓰러짐) 상태로 전환되어
// 다른 플레이어가 소생시키기 전까지 유지된다.
public class PlayerHealth : NetworkBehaviour, IDamageable
{
    [Header("HP 설정 (임시 기본값, 추후 밸런싱 이슈로 조정)")]
    [SerializeField] private float _maxHp = 100f;
    [SerializeField] private float _decayPerSecond = 1f; //1초당 감소 HP 수치
    [SerializeField] private float _reviveHpAmount = 50f; //쓰러진 플레이어 살릴때 회복 수치
    [SerializeField] private float _alienAttackDamage = 20f; //외계인 공격 1회당 감소 HP 수치

    private readonly NetworkVariable<float> _currentHp =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _isDowned =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 외부에서 변경 감지 구독
    public event Action<float, float> HpChanged;
    public event Action<bool> DownedStateChanged;

    public float MaxHp => _maxHp;
    public float CurrentHp => _currentHp.Value;
    public bool IsDowned => _isDowned.Value;

    // 본부(HqSafeZone) 트리거 안에 있는 동안은 감소를 멈춘다.
    private bool _isInHeadquarters;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _currentHp.Value = _maxHp;
        }

        _currentHp.OnValueChanged += HandleHpChanged;
        _isDowned.OnValueChanged += HandleDownedStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        _currentHp.OnValueChanged -= HandleHpChanged;
        _isDowned.OnValueChanged -= HandleDownedStateChanged;
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned || _isDowned.Value || _isInHeadquarters) return;

        // 로딩(NPC/단서 스폰, 몽타주 로딩 등) 도중에는 라운드가 아직 InRound가 아니므로 감소하지 않는다.
        if (RoundManager.Instance == null || RoundManager.Instance.CurrentState != RoundState.InRound) return;

        TakeDamage(_decayPerSecond * Time.deltaTime); //가만히 있어도 HP감소
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsServer && other.GetComponent<HqSafeZone>() != null) { _isInHeadquarters = true; }
    }

    private void OnTriggerExit(Collider other)
    {
        if (IsServer && other.GetComponent<HqSafeZone>() != null) { _isInHeadquarters = false; }
    }

    // 외부(공격 시스템 등)에서 데미지를 입힐 때 호출하는 공개 진입점. 서버에서만 호출 가능하다.
    public void TakeDamage(float amount)
    {
        if (!IsServer)
        {
            Debug.LogError("[PlayerHealth] TakeDamage는 서버에서만 호출할 수 있습니다.");
            return;
        }

        if (_isDowned.Value || amount <= 0f) return;

        _currentHp.Value = Mathf.Max(0f, _currentHp.Value - amount);

        if (_currentHp.Value <= 0f)
        {
            _isDowned.Value = true;
        }
    }

    // 외계인(복제체) 공격 시스템이 호출할 진입점. 실제 공격 판정/AI 로직은 이번 범위 밖이라 아직 없음.
    public void TakeAlienAttackDamage()
    {
        TakeDamage(_alienAttackDamage);
    }

    // 쓰러진 플레이어를 다른 플레이어가 소생시킬 때 호출한다.
    public void Revive()
    {
        if (!IsServer)
        {
            Debug.LogError("[PlayerHealth] Revive는 서버에서만 호출할 수 있습니다.");
            return;
        }

        if (!_isDowned.Value) return;

        _currentHp.Value = _reviveHpAmount;
        _isDowned.Value = false;
    }

    private void HandleHpChanged(float previousValue, float newValue)
    {
        HpChanged?.Invoke(previousValue, newValue);
    }

    private void HandleDownedStateChanged(bool previousValue, bool newValue)
    {
        DownedStateChanged?.Invoke(newValue);
    }
}
