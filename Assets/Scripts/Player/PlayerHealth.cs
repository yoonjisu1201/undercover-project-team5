using System;
using Unity.Netcode;
using UnityEngine;

// 플레이어의 HP를 관리한다. 시간이 지나면 자연 감소하고, 0이 되면 죽지 않고 다운(쓰러짐) 상태로 전환되어
// 다른 플레이어가 소생시키기 전까지 유지된다.
public class PlayerHealth : NetworkBehaviour, IDamageable
{
    [Header("HP 설정 (임시 기본값, 추후 밸런싱 이슈로 조정)")]
    [SerializeField] private float _maxHp = 100f;
    [SerializeField] private float _decayPerSecond = 2f; //1초당 감소 HP 수치
    [SerializeField] private float _reviveHpAmount = 20f; //쓰러진 플레이어 살릴때 회복 수치
    [SerializeField] private float _alienAttackDamage = 20f; //외계인 공격 1회당 감소 HP 수치

    // #663: 체력 시스템 제거 전까지 자연 감소를 꺼 둔다. 되돌릴 때는 이 값을 켜면 된다.
    [SerializeField] private bool _enableHpDecay;

    private readonly NetworkVariable<float> _currentHp =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _isDowned =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 외부에서 변경 감지 구독
    public event Action<float, float> HpChanged;
    // #392: 다운과 소생의 전환 방향을 구독자가 구분할 수 있도록 이전 값과 현재 값을 함께 전달한다.
    public event Action<bool, bool> DownedStateChanged;

    public float MaxHp => _maxHp;
    public float CurrentHp => _currentHp.Value;
    public bool IsDowned => _isDowned.Value;

    // 외계인 복제체 등 외부 AI가 타겟 유효성(본부 안전구역 여부)을 확인할 때 쓴다.
    public bool IsInHeadquarters => _isInHeadquarters;

    // 디버그 메뉴 전용 무적 상태.
    public bool IsDebugInvincible { get; private set; }

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
        if (!_enableHpDecay) return;

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
        if (IsDebugInvincible) return;

        _currentHp.Value = Mathf.Max(0f, _currentHp.Value - amount);

        if (_currentHp.Value <= 0f)
        {
            _isDowned.Value = true;
            RoundManager.Instance?.ReportPlayerDowned();
        }
    }
    
    // 외부(공격 시스템 등)에서 체력을 회복시킬 때 호출하는 공개 진입점. 서버에서만 호출 가능하다.
    // 실제로 얼마나 회복되었는지 반환한다.
    public float RestoreHealth(float amount) {
        if (!IsServer)
        {
            Debug.LogError("[PlayerHealth] RestoreHealth는 서버에서만 호출할 수 있습니다.");
            return 0f;
        }

        if (_isDowned.Value || amount <= 0f) return 0f;
        if (IsDebugInvincible) return 0f;
        
        // 실제 회복량 계산. 매개변수로 들어온 회복량과 최대체력 - 현재체력의 차 중 더 작은 값 사용
        float healAmount = Mathf.Min(amount, _maxHp - _currentHp.Value);
        _currentHp.Value += healAmount;
        
        return healAmount;
    }

    // 가해자 위치를 아는 피해 진입점. 실제로 깎인 만큼만 피격 화면 연출을 맞은 본인에게 띄운다.
    // 서버에서만 호출 가능하다.
    // #469: 자연 감소는 이 경로를 타지 않으므로, 가만히 있어도 화면이 붉어지는 일이 없다.
    public void TakeDamage(float amount, Vector3 sourcePosition)
    {
        if (!IsServer)
        {
            Debug.LogError("[PlayerHealth] TakeDamage는 서버에서만 호출할 수 있습니다.");
            return;
        }

        // 무적·다운 등으로 피해가 실제로 적용되지 않았으면 연출도 띄우지 않는다.
        // 적용 여부는 TakeDamage가 판단하므로 체력 변화로 확인한다.
        float hpBeforeDamage = _currentHp.Value;
        TakeDamage(amount);

        if (_currentHp.Value >= hpBeforeDamage || !IsSpawned)
        {
            return;
        }

        PlayHitEffectRpc(sourcePosition);
    }

    // 외계인(복제체) 공격 시스템이 호출할 진입점.
    // sourcePosition: 공격 판정이 일어난 위치. 피격 방향 표시에 쓴다.
    public void TakeAlienAttackDamage(Vector3 sourcePosition)
    {
        TakeDamage(_alienAttackDamage, sourcePosition);
    }

    // #469: 피격 화면 연출은 맞은 본인 화면에만 필요하므로 오너에게만 보낸다.
    [Rpc(SendTo.Owner)]
    private void PlayHitEffectRpc(Vector3 sourcePosition)
    {
        // PlayScene 밖(연출 UI가 없는 씬)에서도 피해가 들어올 수 있으므로 없으면 조용히 넘긴다.
        if (HitScreenEffect.Instance == null)
        {
            return;
        }

        HitScreenEffect.Instance.PlayHit(sourcePosition);
    }

    // 디버그 메뉴 전용: 무적 상태를 설정한다. 서버에서만 호출 가능하다.
    public void SetDebugInvincible(bool invincible)
    {
        if (!IsServer) return;
        IsDebugInvincible = invincible;
    }

    // 라운드 시작/재시작 시 체력을 최대치로 되돌리고 다운 상태를 해제한다. 서버에서만 호출 가능하다.
    public void ResetForNewRound()
    {
        if (!IsServer) return;

        _currentHp.Value = _maxHp;
        _isDowned.Value = false;
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
        // #392: PlayerHealth가 받은 NetworkVariable 변경값을 애니메이션 구독자까지 그대로 전달한다.
        DownedStateChanged?.Invoke(previousValue, newValue);
    }
}
