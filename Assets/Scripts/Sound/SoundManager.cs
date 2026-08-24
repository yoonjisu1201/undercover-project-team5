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

        _globalSource.PlayOneShot(sound.Clip, sound.Volume);
    }

    // 특정 위치에서 나는 소리를 낸다. SoundData의 MaxDistance를 넘으면 들리지 않는다.
    public void PlayAt(SoundKey key, Vector3 position)
    {
        if (!TryGetPlayable(key, out SoundData sound)) return;

        // 3D로 설정하지 않은 데이터는 위치를 무시하고 그냥 낸다. 설정 실수로 소리가 사라지는 것보다 낫다.
        if (!sound.Is3D)
        {
            Play(key);
            return;
        }

        AudioSource source = GetFreeSpatialSource();
        source.transform.position = position;
        source.clip = sound.Clip;
        source.volume = sound.Volume;
        source.maxDistance = sound.MaxDistance;
        source.outputAudioMixerGroup = ResolveGroup(sound.Group);
        source.Play();
    }

    // 키가 등록되지 않았거나 클립이 비어 있으면 재생하지 않는다.
    // 둘 다 사운드 에셋이 아직 없는 정상 상태라서 경고를 남기지 않는다.
    private bool TryGetPlayable(SoundKey key, out SoundData sound)
    {
        return _soundByKey.TryGetValue(key, out sound) && sound.Clip != null;
    }

    private AudioSource GetFreeSpatialSource()
    {
        foreach (AudioSource source in _spatialSources)
        {
            if (!source.isPlaying) return source;
        }

        AudioSource created = CreateSource($"Spatial_{_spatialSources.Count}", spatialBlend: 1f);
        created.rolloffMode = AudioRolloffMode.Logarithmic;
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
