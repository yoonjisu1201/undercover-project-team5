using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

// 사운드 키로 클립을 찾아 재생한다.
// 클립이 아직 등록되지 않은 키는 조용히 넘어간다. 사운드 에셋이 준비되기 전에도
// 호출부를 미리 심어둘 수 있게 하려는 것이다.
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance;

    [SerializeField] private SoundData[] _sounds;

    [Header("믹서 그룹 (GameAudioMixer의 SFX / BGM을 넣는다)")]
    [SerializeField] private AudioMixerGroup _sfxGroup;
    [SerializeField] private AudioMixerGroup _bgmGroup;

    // 3D 감쇠가 시작되는 거리. 이보다 가까우면 볼륨이 줄지 않는다.
    private const float SpatialMinDistance = 1f;

    private Dictionary<SoundKey, SoundData> _soundByKey;

    // 위치가 없는 소리용.
    private AudioSource _globalSource;

    // 위치가 있는 소리용. 놀고 있는 소스가 없으면 하나 더 만들어 이후 재사용한다.
    private readonly List<AudioSource> _spatialSources = new();

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _soundByKey = new Dictionary<SoundKey, SoundData>();
        foreach (SoundData sound in _sounds)
        {
            if (sound == null || sound.Key == SoundKey.None) continue;

            _soundByKey[sound.Key] = sound;
        }

        _globalSource = CreateSource("Global", spatialBlend: 0f);
        _globalSource.outputAudioMixerGroup = _sfxGroup;
    }

    // 화면에 고정된 소리(UI 클릭, 라운드 알림 등)를 낸다.
    public void Play(SoundKey key)
    {
        if (!TryGetPlayable(key, out SoundData sound)) return;

        // 이 소스는 여러 소리가 겹쳐 나므로 피치가 서로 영향을 준다. UI 클릭처럼 짧은 소리에는
        // 문제가 되지 않고, 피치를 설정하지 않은 데이터는 1이라 기존 동작 그대로다.
        _globalSource.pitch = sound.Pitch;
        _globalSource.PlayOneShot(sound.Clip, sound.Volume);
    }

    // 특정 위치에서 나는 소리를 낸다. SoundData의 MaxDistance를 넘으면 들리지 않는다.
    //
    // 재생에 쓴 소스를 돌려준다. 발소리처럼 중간에 끊어야 하는 소리는 이 소스를 들고 있다가
    // Stop() 하면 된다. 끊을 필요가 없으면 반환값을 무시해도 된다.
    public AudioSource PlayAt(SoundKey key, Vector3 position)
    {
        if (!TryGetPlayable(key, out SoundData sound)) return null;

        // 3D로 설정하지 않은 데이터는 위치를 무시하고 그냥 낸다. 설정 실수로 소리가 사라지는 것보다 낫다.
        if (!sound.Is3D)
        {
            Play(key);
            return null;
        }

        AudioSource source = GetFreeSpatialSource();
        source.transform.position = position;
        source.clip = sound.Clip;
        source.pitch = sound.Pitch;
        source.volume = sound.Volume;
        source.maxDistance = sound.MaxDistance;
        source.outputAudioMixerGroup = ResolveGroup(sound.Group);
        source.Play();
        return source;
    }

    // 이 클립이 해당 키의 것인지. 재생 중인 소리를 끊기 전에 "내가 튼 소리가 맞는지" 확인할 때 쓴다.
    // 소스는 돌려쓰기 때문에 그 사이 다른 소리가 차지했을 수 있다.
    public bool Owns(SoundKey key, AudioClip clip)
    {
        if (clip == null || !_soundByKey.TryGetValue(key, out SoundData sound))
        {
            return false;
        }

        return sound.Contains(clip);
    }

    // 키가 등록되지 않았거나 클립이 비어 있으면 재생하지 않는다.
    // 둘 다 사운드 에셋이 아직 없는 정상 상태라서 경고를 남기지 않는다.
    private bool TryGetPlayable(SoundKey key, out SoundData sound)
    {
        // Clip 은 부를 때마다 무작위로 고르므로, 확인에는 HasClip 을 쓴다.
        return _soundByKey.TryGetValue(key, out sound) && sound.HasClip;
    }

    private AudioSource GetFreeSpatialSource()
    {
        foreach (AudioSource source in _spatialSources)
        {
            if (!source.isPlaying) return source;
        }

        AudioSource created = CreateSource($"Spatial_{_spatialSources.Count}", spatialBlend: 1f);
        // 로그 감쇠는 MaxDistance 에서 0이 되지 않는다. Unity 가 그 지점의 볼륨을 그 너머로도
        // 유지해서, 12m 설정이면 맵 반대편에서도 0.08배로 계속 들린다. 발소리처럼 "멀면 안 들려야"
        // 하는 소리에는 맞지 않는다. 게다가 로그 곡선은 3m 에서 이미 0.33배라 거리감을 주는
        // 중간 구간이 거의 없다. 선형은 MaxDistance 에서 정확히 0이 되고 감쇠도 고르다.
        created.rolloffMode = AudioRolloffMode.Linear;
        created.minDistance = SpatialMinDistance;
        _spatialSources.Add(created);

        return created;
    }

    private AudioSource CreateSource(string sourceName, float spatialBlend)
    {
        GameObject host = new GameObject(sourceName);
        host.transform.SetParent(transform, false);

        AudioSource source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = spatialBlend;

        return source;
    }

    private AudioMixerGroup ResolveGroup(SoundGroup group)
    {
        return group == SoundGroup.Bgm ? _bgmGroup : _sfxGroup;
    }
}
