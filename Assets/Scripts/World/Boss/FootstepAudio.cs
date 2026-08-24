using UnityEngine;

// 발소리를 3D 사운드로 재생한다. 플레이어와 보스가 같은 구조라 한 컴포넌트로 쓴다.
//
// 모든 클라이언트에서 각자 돈다. 서버가 RPC로 알리지 않는 이유는, 트랜스폼이 이미 동기화돼 있어서
// 각 클라이언트가 위치 변화만 봐도 같은 판단을 할 수 있기 때문이다. 매 발자국마다 RPC를 보내면
// 인원수 × 초당 2~3회의 트래픽이 계속 흐른다.
//
// NoiseSystem(보스 감지용)과는 별개다. 이쪽은 사람에게 들려주는 피드백이고, 그쪽은 게임플레이 판정이다.
// 둘을 하나로 묶으면 소리가 없으면 감지도 안 되거나, 반대로 감지 수치를 바꾸면 소리 간격이 같이 바뀐다.
public class FootstepAudio : MonoBehaviour
{
    [SerializeField] private AudioSource _source;

    [Header("클립")]
    [SerializeField] private AudioClip[] _walkClips = new AudioClip[0];
    [SerializeField] private AudioClip[] _runClips = new AudioClip[0];

    [Header("간격 (초)")]
    [SerializeField, Min(0.05f)] private float _walkInterval = 0.5f;
    [SerializeField, Min(0.05f)] private float _runInterval = 0.32f;

    // 걷기 5, 달리기 7.5(=5 × 1.5), 카트 3 이므로 6.2면 걷기와 달리기가 갈린다.
    [Header("판정")]
    [SerializeField, Min(0f)] private float _runSpeedThreshold = 6.2f;

    // 이 속도 아래는 멈춴 것으로 본다. 물리 흔들림으로 발소리가 나는 것을 막는다.
    [SerializeField, Min(0f)] private float _idleSpeedThreshold = 0.6f;

    [Header("변화")]
    [SerializeField] private Vector2 _pitchRange = new Vector2(0.94f, 1.06f);

    private Vector3 _lastPosition;
    private float _nextStepTime;

    private void Awake()
    {
        if (_source == null)
        {
            _source = GetComponent<AudioSource>();
        }

        if (_source == null)
        {
            Debug.LogWarning($"[발소리] '{name}'에 AudioSource가 없어 발소리가 나지 않습니다.", this);
            enabled = false;
            return;
        }

        _lastPosition = transform.position;
    }

    private void Update()
    {
        Vector3 position = transform.position;
        float speed = Vector3.Distance(position, _lastPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        _lastPosition = position;

        if (speed < _idleSpeedThreshold)
        {
            // 멈추면 다음 발소리를 바로 낼 수 있게 해서, 다시 걸을 때 한 박자 늦지 않게 한다.
            _nextStepTime = 0f;
            return;
        }

        if (Time.time < _nextStepTime)
        {
            return;
        }

        bool running = speed >= _runSpeedThreshold;
        _nextStepTime = Time.time + (running ? _runInterval : _walkInterval);
        PlayOne(running ? _runClips : _walkClips);
    }

    private void PlayOne(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
        {
            return;
        }

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null)
        {
            return;
        }

        // 같은 클립이 반복되면 기계적으로 들리므로 음높이를 조금 흔든다.
        _source.pitch = Random.Range(_pitchRange.x, _pitchRange.y);
        _source.PlayOneShot(clip);
    }
}
