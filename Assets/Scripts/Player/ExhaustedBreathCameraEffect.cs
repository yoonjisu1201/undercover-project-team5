using UnityEngine;

// 스태미나 심장 박동에 맞춰 로컬 카메라에 합성할 호흡 오프셋을 계산한다.
// 실제 Transform 적용은 다른 카메라 연출과 충돌하지 않도록 PlayerCameraController가 담당한다.
public class ExhaustedBreathCameraEffect : MonoBehaviour
{
    [Header("탈진 호흡 흔들림")]
    [Tooltip("헐떡임 1회 주기. 심장 박동보다 느려야 호흡으로 읽힌다.")]
    [SerializeField, Min(0.01f)] private float _breathsPerSecond = 1.1f;

    [Tooltip("위아래로 흔들리는 거리(m). 크게 주면 멀미가 난다.")]
    [SerializeField, Min(0f)] private float _bobDistance = 0.014f;

    [Tooltip("고개를 끄덕이는 각도.")]
    [SerializeField, Min(0f)] private float _pitchDegrees = 0.35f;

    [Tooltip("흔들림이 올라오는 시간(초).")]
    [SerializeField, Min(0.01f)] private float _fadeIn = 0.4f;

    [Tooltip("흔들림이 잦아드는 시간(초).")]
    [SerializeField, Min(0.01f)] private float _fadeOut = 1.2f;

    [Tooltip("스태미나가 부족한 단계의 세기.")]
    [SerializeField, Range(0f, 1f)] private float _tiredScale = 0.5f;

    [Tooltip("스태미나가 거의 소진된 단계의 세기.")]
    [SerializeField, Range(0f, 1f)] private float _exhaustedScale = 0.7f;

    private PlayerHeartbeat _heartbeat;
    private float _phase;
    private float _weight;

    private void Awake()
    {
        _heartbeat = GetComponent<PlayerHeartbeat>();
    }

    public void Evaluate(float deltaTime, out float bobOffset, out float pitchOffset)
    {
        float target = ResolveTargetWeight();
        float fadeDuration = target > _weight ? _fadeIn : _fadeOut;
        _weight = Mathf.MoveTowards(_weight, target, deltaTime / fadeDuration);

        if (_weight <= 0f)
        {
            bobOffset = 0f;
            pitchOffset = 0f;
            return;
        }

        _phase = Mathf.Repeat(_phase + deltaTime * _breathsPerSecond, 1f);
        float wave = Mathf.Sin(_phase * Mathf.PI * 2f);

        bobOffset = wave * _bobDistance * _weight;
        pitchOffset = wave * _pitchDegrees * _weight;
    }

    public void ResetEffect()
    {
        _phase = 0f;
        _weight = 0f;
    }

    private float ResolveTargetWeight()
    {
        if (_heartbeat == null)
        {
            return 0f;
        }

        return _heartbeat.CurrentKey switch
        {
            SoundKey.Player_HeartBeat_Tired => _tiredScale,
            SoundKey.Player_HeartBeat_Exhausted => _exhaustedScale,
            _ => 0f,
        };
    }
}
