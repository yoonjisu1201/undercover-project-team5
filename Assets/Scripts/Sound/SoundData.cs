using UnityEngine;

// 어느 믹서 그룹으로 보낼지. 설정 화면의 볼륨 슬라이더가 그룹 단위로 걸려 있다.
public enum SoundGroup
{
    Sfx = 0,
    Bgm = 1,
}

[CreateAssetMenu(menuName = "Sound/SoundData", fileName = "NewSoundData")]
public class SoundData : ScriptableObject
{
    [SerializeField] private SoundKey _key;

    // 같은 종류의 소리를 여러 개 넣으면 재생할 때마다 하나를 무작위로 고른다.
    // 발소리처럼 짧게 반복되는 소리는 하나만 쓰면 금방 기계적으로 들린다.
    // 여러 발자국을 한 파일에 이어 붙이면 안 된다 — 멈춰도 남은 발자국이 계속 재생되고,
    // 재생 간격을 파일이 정해버려서 걷기/달리기 속도에 맞출 수 없다.
    [SerializeField] private AudioClip[] _clips = new AudioClip[0];

    [SerializeField, Range(0f, 1f)] private float _volume = 1f;
    [SerializeField] private SoundGroup _group = SoundGroup.Sfx;

    // 재생마다 음높이를 이 범위에서 흔든다. 클립이 몇 개뿐이어도 반복감이 줄어든다.
    // 기본값은 1~1이라 설정하지 않으면 원음 그대로 난다.
    [SerializeField] private Vector2 _pitchRange = new Vector2(1f, 1f);

    [Header("이어지는 소리")]
    // 재생 방식을 데이터가 정한다. Is 3D 가 Play / PlayAt 을 가르는 것과 같은 자리다.
    [Tooltip("체크하면 이 소리를 끊기지 않고 반복해서 낸다. BGM·심장 박동처럼 상태가 유지되는 "
        + "동안 계속 나야 하는 소리에 켠다. 끄면 PlayLoop 로 불러도 나지 않는다.")]
    [SerializeField] private bool _isLoop;

    [Header("3D (위치가 있는 소리)")]
    [SerializeField] private bool _is3D;
    // 이 거리를 넘으면 들리지 않는다. 발소리처럼 근처에서만 들려야 하는 소리를 여기서 조정한다.
    [SerializeField, Min(1f)] private float _maxDistance = 12f;

    public SoundKey Key => _key;

    // 이번에 재생할 클립. 부를 때마다 다시 고르므로, 재생 직전에 한 번만 읽어야 한다.
    //
    // 무작위로 고른 자리가 비어 있으면 다음 자리로 넘어간다. 인스펙터에서 슬롯을 늘려놓고
    // 아직 안 채운 경우에 소리가 가끔 안 나는 것을 막는다.
    public AudioClip Clip
    {
        get
        {
            if (_clips == null || _clips.Length == 0)
            {
                return null;
            }

            int start = Random.Range(0, _clips.Length);
            for (int i = 0; i < _clips.Length; i++)
            {
                AudioClip clip = _clips[(start + i) % _clips.Length];
                if (clip != null)
                {
                    return clip;
                }
            }

            return null;
        }
    }

    // 재생 가능한 클립이 하나라도 있는지. 무작위 선택 없이 확인만 할 때 쓴다.
    public bool HasClip
    {
        get
        {
            if (_clips == null)
            {
                return false;
            }

            foreach (AudioClip clip in _clips)
            {
                if (clip != null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public float Pitch => Random.Range(_pitchRange.x, _pitchRange.y);

    // 이 소리에 속한 클립인지. 재생 중인 소리가 무엇이었는지 되짚을 때 쓴다.
    public bool Contains(AudioClip clip)
    {
        if (clip == null || _clips == null)
        {
            return false;
        }

        foreach (AudioClip candidate in _clips)
        {
            if (candidate == clip)
            {
                return true;
            }
        }

        return false;
    }

    public float Volume => _volume;
    public SoundGroup Group => _group;
    public bool IsLoop => _isLoop;
    public bool Is3D => _is3D;
    public float MaxDistance => _maxDistance;
}
