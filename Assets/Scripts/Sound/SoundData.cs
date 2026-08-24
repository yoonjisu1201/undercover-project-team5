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
    [SerializeField] private AudioClip _clip;
    [SerializeField, Range(0f, 1f)] private float _volume = 1f;
    [SerializeField] private SoundGroup _group = SoundGroup.Sfx;

    [Header("3D (위치가 있는 소리)")]
    [SerializeField] private bool _is3D;
    // 이 거리를 넘으면 들리지 않는다. 발소리처럼 근처에서만 들려야 하는 소리를 여기서 조정한다.
    [SerializeField, Min(1f)] private float _maxDistance = 12f;

    public SoundKey Key => _key;
    public AudioClip Clip => _clip;
    public float Volume => _volume;
    public SoundGroup Group => _group;
    public bool Is3D => _is3D;
    public float MaxDistance => _maxDistance;
}
