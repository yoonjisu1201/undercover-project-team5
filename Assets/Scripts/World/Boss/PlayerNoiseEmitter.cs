using Unity.Netcode;
using UnityEngine;

// 플레이어가 내는 소리를 소음으로 바꿔 NoiseSystem에 보고한다. 보스 감지의 입력이 된다.
//
// 서버에서만 판정한다. 이동 자체는 오너 클라이언트가 하지만 서버의 트랜스폼도 함께 갱신되므로,
// 위치 변화량으로 움직였는지를 알 수 있다. 달리기·점프·음소거·발화처럼 클라이언트만 아는 상태는
// 오너가 NetworkVariable로 올린다.
//
// 사람에게 들려주는 발소리는 PlayerMoveSample 이 SoundManager 로 재생한다. 이쪽은 판정만 한다.
[RequireComponent(typeof(PlayerStamina))]
public class PlayerNoiseEmitter : NetworkBehaviour
{
    // 마이크 상태를 확인하는 간격(초). 말하기 시작한 것을 알아채는 지연이 이 값이다.
    private const float MicCheckInterval = 0.1f;

    // 발소리 간격. 매 프레임 보고하면 목록이 폭증하고, 너무 길면 보스가 놓친다.
    [SerializeField, Min(0.05f)] private float _stepInterval = 0.4f;

    // 이 간격 동안 이 거리보다 덜 움직였으면 멈춴 것으로 본다.
    [SerializeField, Min(0f)] private float _movedThreshold = 0.15f;

    // 들리는 거리. 소리는 벽을 통과하는 유일한 감각이라 보스 시야(7m)와의 관계로 잡는다.
    // 시야보다 작으면 눈이 먼저 잡아서 청각이 하는 일이 없어진다.
    //
    // 달리기 26m 의 근거: 질주는 스태미나상 5초가 전부고 그동안 15m 가 벌어진다. 발각 거리 5m
    // 에서 시작하면 20m 지점에서 스태미나가 떨어지므로, 26m 는 질주만으로 아슬아슬하게
    // 못 벗어난다. 모퉁이를 함께 써야 하는 거리다.
    [Header("들리는 거리 (m)")]
    [SerializeField, Min(0f)] private float _walkRadius = 10f;
    [SerializeField, Min(0f)] private float _sprintRadius = 26f;

    [Tooltip("착지 소음. 한 번에 크게 나는 소리라 걷기보다 훨씬 멀리 들린다.")]
    [SerializeField, Min(0f)] private float _landingRadius = 18f;

    [Tooltip("목소리 소음. 보스가 가까울 때 말을 참게 만드는 값이다.")]
    [SerializeField, Min(0f)] private float _voiceRadius = 18f;

    [Tooltip("말하는 동안 소음을 보고하는 간격(초).")]
    [SerializeField, Min(0.05f)] private float _voiceInterval = 0.5f;

    [Header("음소거 보정")]
    [Tooltip("마이크를 끄면 다른 소음이 커진다. 음소거로 목소리 소음을 없애는 것이 이득만 되지 않게 한다.")]
    [SerializeField] private bool _muteIncreasesNoise = true;

    [SerializeField, Min(1f)] private float _mutedNoiseMultiplier = 1.8f;

    // 마이크 음소거와 발화 여부는 각 클라이언트만 아는 값이라 서버로 올려야 한다.
    // 오너가 직접 쓰므로 RPC가 필요 없다.
    private readonly NetworkVariable<bool> _isMicMuted =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> _isSpeaking =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private PlayerStamina _stamina;
    private PlayerMoveSample _movement;
    private Vector3 _lastSamplePosition;
    private float _nextStepTime;
    private float _nextVoiceTime;
    private float _nextMicCheckTime;
    private bool _wasJumping;

    // 이 플레이어가 내는 소음에 곱할 배율. 문 여는 소리처럼 다른 곳에서 보고하는 소음도 이 값을 쓴다.
    public float NoiseMultiplier =>
        _muteIncreasesNoise && _isMicMuted.Value ? _mutedNoiseMultiplier : 1f;

    private void Awake()
    {
        _stamina = GetComponent<PlayerStamina>();
        _movement = GetComponent<PlayerMoveSample>();
    }

    public override void OnNetworkSpawn()
    {
        // 오너는 자기 마이크 상태만 올린다. 판정은 서버가 한다.
        if (IsOwner)
        {
            ReportMicState();
        }

        if (!IsServer)
        {
            return;
        }

        _lastSamplePosition = transform.position;
        _nextStepTime = Time.time + _stepInterval;
    }

    private void Update()
    {
        if (IsOwner)
        {
            ReportMicState();
        }

        if (IsServer)
        {
            ReportMovementNoise();
            ReportLandingNoise();
            ReportVoiceNoise();
        }
    }

    // Vivox 발화 판정은 채널 참가자 목록을 훑는다. 매 프레임 볼 필요가 없어서 간격을 둔다.
    private void ReportMicState()
    {
        if (Time.time < _nextMicCheckTime)
        {
            return;
        }

        _nextMicCheckTime = Time.time + MicCheckInterval;

        bool muted = VivoxManager.IsMicMuted;
        if (_isMicMuted.Value != muted)
        {
            _isMicMuted.Value = muted;
        }

        // 음소거 중에는 음량이 올라오지 않으므로 자연히 false 가 된다.
        bool speaking = VivoxManager.Instance != null && VivoxManager.Instance.IsLocalSpeaking;
        if (_isSpeaking.Value != speaking)
        {
            _isSpeaking.Value = speaking;
        }
    }

    private void ReportMovementNoise()
    {
        if (Time.time < _nextStepTime)
        {
            return;
        }

        _nextStepTime = Time.time + _stepInterval;

        Vector3 position = transform.position;
        float moved = Vector3.Distance(position, _lastSamplePosition);
        _lastSamplePosition = position;

        if (moved < _movedThreshold)
        {
            return;
        }

        // 달리기는 스태미나가 판정한다. 위치 변화량으로 속도를 추정하면
        // 프레임 흔들림이나 넉백까지 달리기로 잡힌다.
        bool sprinting = _stamina.IsSprinting;
        Report(position, sprinting ? _sprintRadius : _walkRadius, sprinting ? "달리기" : "걷기");
    }

    // 점프는 뛰어오를 때보다 내려앉을 때 소리가 난다. 점프 상태가 풀리는 순간이 착지다.
    private void ReportLandingNoise()
    {
        if (_movement == null)
        {
            return;
        }

        bool jumping = _movement.IsJumping;
        if (_wasJumping && !jumping)
        {
            Report(transform.position, _landingRadius, "착지");
        }

        _wasJumping = jumping;
    }

    private void ReportVoiceNoise()
    {
        if (!_isSpeaking.Value || Time.time < _nextVoiceTime)
        {
            return;
        }

        _nextVoiceTime = Time.time + _voiceInterval;
        Report(transform.position, _voiceRadius, "목소리");
    }

    // 음소거 보정은 한곳에서만 곱해야 종류별로 빠지는 곳이 생기지 않는다.
    private void Report(Vector3 position, float radius, string kind)
    {
        // 표적이 될 수 없는 상태면 아무 소음도 내지 않는다.
        //
        // 쓰러지면 이동·착지 소음은 입력이 막혀 저절로 멈추지만 목소리는 그렇지 않다. Vivox 마이크는
        // 다운과 무관하게 살아 있어서, 쓰러진 사람이 계속 말하면 보스가 그 소리를 듣고 시신 주변을
        // 떠나지 못한다. 소생하면 조건이 다시 참이 되므로 따로 되돌릴 것은 없다.
        //
        // 판정을 SurvivorRegistry 에 맡기는 이유는 보스 감지·기억과 조건을 어긋나지 않게 두려는 것이다.
        // 여기서 IsDowned 만 따로 보면 본부에 있는 사람이 내는 소음이 또 다른 예외로 남는다.
        if (!SurvivorRegistry.IsActive(gameObject))
        {
            return;
        }

        float multiplier = NoiseMultiplier;
        NoiseSystem.Report(position, radius * multiplier, multiplier > 1f ? kind + "(음소거)" : kind);
    }
}
