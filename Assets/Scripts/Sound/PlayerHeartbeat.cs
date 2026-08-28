using Unity.Netcode;
using UnityEngine;

// 보스가 이 사람을 어떻게 인지하고 있는지. 서버만 판정할 수 있어서 오너에게 따로 내려보낸다.
// 값이 클수록 위험하다 — 단계를 비교로 고르기 때문에 순서를 지켜야 한다.
public enum BossThreat : byte
{
    None = 0,
    Searching = 1,  // 놓쳤지만 아직 이 사람을 찾고 있다
    Spotted = 2,    // 지금 보이거나 감지되고 있다
}

// 자기 심장 소리. 스태미나가 빨간 구간에 들어가거나 보스가 쫓아오면 뛰기 시작하고,
// 상황이 풀리면 서서히 사라진다.
//
// 오너에게만 들린다. 남의 심장 소리는 들릴 이유가 없어서 2D 로 낸다.
//
// 단계마다 클립이 다르다(BPM 이 다름). 단계가 바뀌면 새 키를 페이드 인 하면서 이전 키를
// 페이드 아웃 해서 크로스페이드로 넘긴다. 뚝 끊고 새로 틀면 박동이 튄다.
public class PlayerHeartbeat : NetworkBehaviour
{
    [Header("스태미나 기준 (남은 비율)")]
    [Tooltip("박동이 시작되는 지점.")]
    [SerializeField, Range(0f, 1f)] private float _tiredRatio = 0.3f;

    [Tooltip("거의 바닥난 지점. 여기부터 더 빠른 박동으로 넘어간다.")]
    [SerializeField, Range(0f, 1f)] private float _exhaustedRatio = 0.07f;

    [Tooltip("90 이 이 비율까지 회복되면 70 으로 내려온다. 시작 기준(7%)보다 훨씬 높게 둬야 "
        + "바닥 근처에서 두 단계가 번갈아 나는 것을 막는다.")]
    [SerializeField, Range(0f, 1f)] private float _exhaustedRecoveredRatio = 0.35f;

    [Tooltip("여기서부터 차오르는 만큼 소리가 옅어진다. 딱 끊지 않고 회복되는 동안 계속 줄어든다.")]
    [SerializeField, Range(0f, 1f)] private float _fadeStartRatio = 0.5f;

    [Tooltip("완전히 조용해지는 비율. 옅어지기 시작하는 지점보다 높게 둬야 줄어드는 구간이 생긴다.")]
    [SerializeField, Range(0f, 1f)] private float _silencedRatio = 0.65f;

    [Header("발각 이후 유지 시간(초)")]
    [Tooltip("화면에서 보스가 사라진 뒤에도 이만큼은 180 을 유지한다. 도망치려면 등을 돌려야 하므로 "
        + "0 으로 두면 도망치는 순간 소리가 내려간다. 반대로 길게 두면 보지도 않은 채 180 이 이어져서 "
        + "'안 보이는데 왜 뛰지'가 된다. 등을 돌리고 달아나는 동안만 덮을 값으로 둔다.")]
    [SerializeField, Min(0f)] private float _spottedHold = 3f;

    [Tooltip("그 뒤 이만큼은 120 을 유지하고 나서 스태미나 판정으로 돌아간다. "
        + "180 에서 곧바로 조용해지면 위험이 끝났다는 신호가 너무 이르게 온다.")]
    [SerializeField, Min(0f)] private float _searchTail = 5f;

    [Header("페이드 시간(초)")]
    [Tooltip("박동이 시작될 때. 갑자기 튀어나오지 않게 짧게 올린다.")]
    [SerializeField, Min(0f)] private float _fadeIn = 0.6f;

    [Tooltip("상황이 풀려 사라질 때. 회복은 천천히 느껴져야 해서 길게 준다.")]
    [SerializeField, Min(0f)] private float _fadeOut = 2f;

    [Tooltip("단계가 바뀔 때. 두 클립이 겹치는 시간이다.")]
    [SerializeField, Min(0f)] private float _fadeSwap = 0.5f;

    private readonly NetworkVariable<BossThreat> _bossThreat =
        new(BossThreat.None, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

    private PlayerStamina _stamina;
    private PlayerHealth _health;

    // 지금 나고 있는 박동. None 이면 아무 소리도 안 난다.
    private SoundKey _current = SoundKey.None;

    // 스태미나 박동이 켜져 있는지. 켜고 끄는 기준이 달라서(빨간 구간 진입 / 회복 지점 도달)
    // 비율만으로는 판단할 수 없다.
    private bool _staminaLatched;

    // 한 번 바닥까지 갔는지. 회복 중에 90 → 70 으로 되돌아가지 않게 하려고 따로 둔다.
    private bool _exhaustedLatched;

    // 이번 판정이 스태미나에서 나온 것인지. 보스에게 쫓기는 중에는 소리를 옅게 만들지 않는다.
    private bool _fromStamina;

    // 마지막으로 발각된 시각. 여기서부터 시간을 재서 180 → 120 → 스태미나로 내려간다.
    // 아직 한 번도 발각되지 않았음을 뜻하는 값으로 시작한다.
    private float _lastSpottedTime = float.NegativeInfinity;

    // 지금 들리는 박동의 BPM. 소리가 안 나면 0 이다. 개인 HUD 의 심박 그래프가 귀에 들리는
    // 박자와 같은 속도로 뛰도록 이 값을 읽어간다. 단계별 BPM 은 SoundKey 주석과 같다.
    public float CurrentBpm
    {
        get
        {
            switch (_current)
            {
                case SoundKey.Player_HeartBeat_Tired: return 70f;
                case SoundKey.Player_HeartBeat_Exhausted: return 90f;
                case SoundKey.Player_HeartBeat_Hiding: return 120f;
                case SoundKey.Player_HeartBeat_Spotted: return 180f;
                default: return 0f;
            }
        }
    }

    // === 개발용 표시가 읽는 값들 ===
    public SoundKey CurrentKey => _current;
    public bool StaminaLatched => _staminaLatched;

    public BossThreat Threat => IsOwner || IsServer ? _bossThreat.Value : BossThreat.None;

    // 마지막 발각 이후 지난 시간. 아직 발각된 적이 없으면 음의 무한대가 나온다.
    public float SinceSpotted => Time.time - _lastSpottedTime;
    public float VolumeScale => ResolveVolumeScale();
    public float SpottedHold => _spottedHold;
    public float SearchTail => _searchTail;

    public float StaminaRatio =>
        _stamina == null || _stamina.MaxStamina <= 0f ? 1f : _stamina.CurrentStamina / _stamina.MaxStamina;

    // 개발용 강제 지정. None 이면 강제하지 않고 원래 판정을 따른다.
    // 보스가 없는 상태에서도 180 BPM 을 들어봐야 하기 때문에 필요하다.
    public SoundKey DebugForcedKey { get; set; } = SoundKey.None;

    private void Awake()
    {
        _stamina = GetComponent<PlayerStamina>();
        _health = GetComponent<PlayerHealth>();
    }

    public override void OnNetworkDespawn()
    {
        // 소리는 SoundManager 가 들고 있어서, 플레이어가 사라져도 알아서 멈추지 않는다.
        Silence();
    }

    // 보스가 서버에서 부른다. 값이 그대로면 NetworkVariable 이 알아서 걸러낸다.
    public void SetBossThreat(BossThreat threat)
    {
        if (!IsServer) return;

        _bossThreat.Value = threat;
    }

    private void Update()
    {
        if (!IsOwner) return;

        SoundKey desired = ResolveDesiredKey();

        if (desired != _current)
        {
            // 사라질 때는 길게, 다른 단계로 넘어갈 때는 짧게.
            if (_current != SoundKey.None)
            {
                SoundManager.Instance?.StopLoop(_current, desired == SoundKey.None ? _fadeOut : _fadeSwap);
            }

            if (desired != SoundKey.None)
            {
                SoundManager.Instance?.PlayLoop(desired, _current == SoundKey.None ? _fadeIn : _fadeSwap);
            }

            _current = desired;
        }

        // 세기는 매 프레임 다시 넣는다. 스태미나가 차오르는 동안 계속 줄어들어야 하기 때문에,
        // 단계가 바뀌는 순간에만 넣으면 그 사이에는 값이 멈춰 있다.
        if (_current != SoundKey.None)
        {
            SoundManager.Instance?.SetLoopScale(_current, ResolveVolumeScale());
        }
    }

    // 지금 내야 할 세기. 스태미나가 옅어지기 시작하는 지점을 넘기면 차오르는 만큼 줄어든다.
    // 보스에게 쫓기는 중에는 줄이지 않는다 — 그때 소리가 옅어지면 위험이 끝난 것처럼 들린다.
    private float ResolveVolumeScale()
    {
        if (!_fromStamina) return 1f;

        return 1f - Mathf.InverseLerp(_fadeStartRatio, _silencedRatio, StaminaRatio);
    }

    // 지금 나야 할 박동. 위험한 쪽이 이긴다 — 보스에게 쫓기는 중이면 스태미나는 따지지 않는다.
    private SoundKey ResolveDesiredKey()
    {
        if (DebugForcedKey != SoundKey.None)
        {
            _fromStamina = false;
            return DebugForcedKey;
        }

        // 쓰러졌으면 심장 소리를 낼 상황이 아니다. 살아나면 다시 판정된다.
        if (_health != null && _health.IsDowned)
        {
            _staminaLatched = false;
            _exhaustedLatched = false;
            _lastSpottedTime = float.NegativeInfinity;
            return SoundKey.None;
        }

        BossThreat threat = _bossThreat.Value;

        _fromStamina = false;

        if (threat == BossThreat.Spotted)
        {
            _lastSpottedTime = Time.time;
            return SoundKey.Player_HeartBeat_Spotted;
        }

        // 발각이 풀려도 한동안은 아직 쫓기는 중으로 본다. 스태미나가 바닥나 더 달릴 수 없는
        // 순간이 가장 조여드는 지점인데, 그때 소리가 스태미나 단계로 내려가면 위험이 끝난
        // 것처럼 들린다. 그래서 이 구간은 스태미나를 아예 보지 않는다.
        float sinceSpotted = Time.time - _lastSpottedTime;

        if (sinceSpotted < _spottedHold)
        {
            return SoundKey.Player_HeartBeat_Spotted;
        }

        // 수색은 이 구간과 같은 소리다. 아직 찾고 있으니 계속 120 으로 둔다.
        if (threat == BossThreat.Searching || sinceSpotted < _spottedHold + _searchTail)
        {
            return SoundKey.Player_HeartBeat_Hiding;
        }

        _fromStamina = true;
        return ResolveStaminaKey();
    }

    private SoundKey ResolveStaminaKey()
    {
        if (_stamina == null || _stamina.MaxStamina <= 0f)
        {
            return SoundKey.None;
        }

        float ratio = _stamina.CurrentStamina / _stamina.MaxStamina;

        // 시작 기준과 해제 기준을 다르게 둔다(히스테리시스). 하나로 두면 회복 중에 비율이 임계값을
        // 오가면서 소리가 켜졌다 꺼졌다 한다.
        if (_staminaLatched)
        {
            if (ratio >= _silencedRatio)
            {
                _staminaLatched = false;
                _exhaustedLatched = false;
                return SoundKey.None;
            }
        }
        else if (ratio <= _tiredRatio)
        {
            _staminaLatched = true;
        }
        else
        {
            return SoundKey.None;
        }

        // 90 은 시작 기준보다 훨씬 높은 지점까지 회복돼야 70 으로 내려온다.
        //
        // 두 기준을 같게 두면, 스태미나가 바닥을 찍는 순간부터 회복이 곧바로 시작되기 때문에
        // 7% 를 스칠 때마다 90 이 한 순간 났다가 바로 70 으로 바뀌어 소리가 덜컹거린다.
        // 35% 까지 끌고 가면 90 이 한 단계로 충분히 이어지고, 거기서 70 으로 한 번만 넘어간다.
        if (ratio >= _exhaustedRecoveredRatio)
        {
            _exhaustedLatched = false;
        }
        else if (ratio <= _exhaustedRatio)
        {
            _exhaustedLatched = true;
        }

        return _exhaustedLatched
            ? SoundKey.Player_HeartBeat_Exhausted
            : SoundKey.Player_HeartBeat_Tired;
    }

    private void Silence()
    {
        if (_current == SoundKey.None) return;

        SoundManager.Instance?.StopLoop(_current);
        _current = SoundKey.None;
        _staminaLatched = false;
        _exhaustedLatched = false;
        _lastSpottedTime = float.NegativeInfinity;
    }
}
