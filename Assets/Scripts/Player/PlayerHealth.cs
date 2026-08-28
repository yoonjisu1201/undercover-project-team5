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

    [Header("HP 자연 회복")]
    [Tooltip("마지막으로 피해를 입은 뒤 회복이 시작되기까지의 시간(초). 카트와 에너지바가 사라져 "
        + "회복 수단이 없어졌으므로 시간이 그 자리를 대신한다.")]
    [SerializeField, Min(0f)] private float _regenDelay = 10f;

    [Tooltip("HP 가 1 오르는 데 걸리는 시간(초). 최대 HP 에 대한 비율이 아니라 고정 속도라서, "
        + "최대치를 바꿔도 회복 속도는 그대로다. PlayerStamina 의 회복도 같은 방식(초당 절대량)이다.")]
    [SerializeField, Min(0.01f)] private float _secondsPerHpRegen = 1f;

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

    // 마지막으로 피해를 입은 뒤 지난 시간. 대기 시간을 넘기면 그때부터 계속 회복된다. 서버 전용이다.
    private float _regenTimer;

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
        TickHpRegen();

        if (!_enableHpDecay) return;

        if (!IsServer || !IsSpawned || _isDowned.Value || _isInHeadquarters) return;

        // 로딩(NPC/단서 스폰, 몽타주 로딩 등) 도중에는 라운드가 아직 InRound가 아니므로 감소하지 않는다.
        if (RoundManager.Instance == null || RoundManager.Instance.CurrentState != RoundState.InRound) return;

        TakeDamage(_decayPerSecond * Time.deltaTime); //가만히 있어도 HP감소
    }

    // 마지막 피해로부터 대기 시간이 지나면 초당 일정 비율로 계속 회복한다.
    // 자연 감소(_enableHpDecay)와는 별개로 동작한다.
    private void TickHpRegen()
    {
        if (!IsServer || !IsSpawned || _isDowned.Value) return;
        if (_secondsPerHpRegen <= 0f) return;

        // 만피에서는 시간을 세지 않는다. 그냥 두면 이미 대기가 끝난 상태로 다치는 순간
        // 곧바로 회복이 이어져서 대기 시간이 무의미해진다.
        if (_currentHp.Value >= _maxHp)
        {
            _regenTimer = 0f;
            return;
        }

        // 로딩(NPC/단서 스폰, 몽타주 로딩 등) 도중에는 라운드가 아직 InRound가 아니므로 회복하지 않는다.
        if (RoundManager.Instance == null || RoundManager.Instance.CurrentState != RoundState.InRound) return;

        _regenTimer += Time.deltaTime;

        if (_regenTimer < _regenDelay) return;

        RestoreHealth(Time.deltaTime / _secondsPerHpRegen);
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

        // 피해를 입으면 자연 회복 주기를 처음부터 다시 센다. 교전 중에 회복이 끼어들지 않게 한다.
        // 자연 감소(_enableHpDecay)를 다시 켜면 이 경로를 매 프레임 타므로 자연 회복은 멈춘다.
        _regenTimer = 0f;

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

        PlayHitSoundRpc();
        PlayHitEffectRpc(sourcePosition);
    }

    // 외계인(복제체) 공격 시스템이 호출할 진입점.
    // sourcePosition: 공격 판정이 일어난 위치. 피격 방향 표시에 쓴다.
    public void TakeAlienAttackDamage(Vector3 sourcePosition)
    {
        TakeDamage(_alienAttackDamage, sourcePosition);
    }

    // 맞는 소리는 화면 연출과 달리 본인만의 것이 아니다. 옆에 있던 팀원도 누가 맞았는지 들어야 한다.
    // 위치는 각 클라이언트가 자기 쪽 트랜스폼에서 읽는다. 이미 동기화돼 있어 인자로 넘길 필요가 없다.
    [Rpc(SendTo.Everyone)]
    private void PlayHitSoundRpc()
    {
        SoundManager.Instance?.PlayAt(SoundKey.Player_Hit, transform.position);
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
        _regenTimer = 0f;
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

        _currentHp.Value = Mathf.Min(_reviveHpAmount, _maxHp);
        _isDowned.Value = false;

        // 일어선 직후에는 숨이 차 있어야 한다. 스태미나를 그대로 두면 쓰러진 자리에서
        // 바로 전력 질주가 가능해서, 소생이 위험을 벗어나는 공짜 수단이 된다.
        GetComponent<PlayerStamina>()?.Deplete();
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
