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

    // 이어지는 소리용. 키 하나가 채널 하나다. 같은 키를 다시 틀 때 소스를 물려쓰려고 계속 들고 있는다.
    // 이어지는 소리는 종류가 몇 개뿐이라 쌓여도 부담이 없다.
    private readonly Dictionary<SoundKey, LoopChannel> _loops = new();

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

    // 이어지는 소리(BGM, 심장 박동처럼 상태가 유지되는 동안 계속 나는 소리)를 낸다.
    //
    // 키 하나가 채널 하나라서 서로 다른 키는 동시에 난다. 한 키를 페이드 인 하면서 다른 키를
    // 페이드 아웃 하면 그대로 크로스페이드가 된다. 심장 박동처럼 단계별로 클립이 갈리는 소리는
    // 이렇게 넘긴다.
    //
    // 이미 나고 있는 키를 다시 넘기면 처음부터 다시 틀지 않고 볼륨만 원래대로 되돌린다.
    // 임계값 근처에서 조건이 깜빡여도 소리가 끊기지 않는다. 매 프레임 불러도 된다.
    //
    // 위치는 지원하지 않는다. 이어지는 소리는 듣는 사람에게 붙어 있는 것들이라, 3D 로 설정된
    // 데이터를 넘겨도 2D 로 난다.
    public void PlayLoop(SoundKey key, float fadeSeconds = 0f)
    {
        if (!TryGetPlayable(key, out SoundData sound)) return;

        // 이어지는 소리로 표시되지 않은 데이터는 루프로 내지 않는다. 단발 클립을 루프로 돌리면
        // 쉼 없이 붙어 나오는데, 그게 실수인지 의도인지 소리만으로는 구분되지 않는다.
        if (!sound.IsLoop) return;

        if (!_loops.TryGetValue(key, out LoopChannel channel))
        {
            channel = new LoopChannel { Source = CreateSource($"Loop_{key}", spatialBlend: 0f) };
            channel.Source.loop = true;
            _loops[key] = channel;
        }

        AudioSource source = channel.Source;

        // 클립과 피치는 재생을 시작할 때만 정한다. 이어지는 소리를 중간에 갈아치우면 튄다.
        if (!source.isPlaying)
        {
            source.clip = sound.Clip;
            source.pitch = sound.Pitch;
            source.outputAudioMixerGroup = ResolveGroup(sound.Group);
            channel.Level = fadeSeconds > 0f ? 0f : 1f;
            // 지난번에 옅게 줄여둔 값이 남아 있으면 새로 틀 때 소리가 안 들린다.
            channel.Scale = 1f;
            source.volume = sound.Volume * channel.Level;
            source.Play();
        }

        // 데이터의 볼륨을 매번 다시 읽는다. 인스펙터에서 조정한 값이 재생 중에도 반영된다.
        channel.Ceiling = sound.Volume;
        channel.TargetLevel = 1f;
        channel.FadeSpeed = fadeSeconds > 0f ? 1f / fadeSeconds : 0f;
    }

    // 이어지는 소리의 볼륨에 곱하는 값(0~1). 페이드와 별개로 밖에서 세기를 조절할 때 쓴다.
    //
    // 페이드는 시간에 따라 줄어들지만, 이건 상태에 따라 줄어든다. 스태미나가 차오르는 만큼
    // 심장 소리가 옅어지는 것처럼, 끝나는 시점이 시간이 아니라 값으로 정해지는 경우다.
    // 둘을 한 값에 섞으면 페이드 도중에 세기가 바뀔 때 진행도가 망가진다.
    public void SetLoopScale(SoundKey key, float scale)
    {
        if (_loops.TryGetValue(key, out LoopChannel channel))
        {
            channel.Scale = Mathf.Clamp01(scale);
        }
    }

    // 이어지는 소리를 멈춘다. fadeSeconds 를 주면 그 시간에 걸쳐 서서히 사라진다.
    public void StopLoop(SoundKey key, float fadeSeconds = 0f)
    {
        if (!_loops.TryGetValue(key, out LoopChannel channel)) return;

        channel.TargetLevel = 0f;

        if (fadeSeconds > 0f)
        {
            channel.FadeSpeed = 1f / fadeSeconds;
            return;
        }

        channel.Level = 0f;
        channel.Source.Stop();
    }

    // 나고 있는 이어지는 소리를 전부 멈춘다. 라운드가 끝나거나 씬을 옮길 때 쓴다.
    // SoundManager 는 씬을 넘어 살아남으므로, 그냥 두면 로비까지 소리를 끌고 간다.
    public void StopAllLoops(float fadeSeconds = 0f)
    {
        foreach (SoundKey key in _loops.Keys)
        {
            StopLoop(key, fadeSeconds);
        }
    }

    // 지금 이 키가 나고 있는지. 사라지는 중인 것도 포함한다.
    public bool IsLooping(SoundKey key)
    {
        return _loops.TryGetValue(key, out LoopChannel channel) && channel.Source.isPlaying;
    }

    private void Update()
    {
        foreach (LoopChannel channel in _loops.Values)
        {
            AudioSource source = channel.Source;
            if (!source.isPlaying) continue;

            // 게임이 멈춰도 소리는 계속 흘러야 하므로 unscaled 를 쓴다.
            channel.Level = channel.FadeSpeed <= 0f
                ? channel.TargetLevel
                : Mathf.MoveTowards(channel.Level, channel.TargetLevel, channel.FadeSpeed * Time.unscaledDeltaTime);

            source.volume = channel.Ceiling * channel.Level * channel.Scale;

            // 다 사라졌으면 소스를 멈춘다. 볼륨 0으로 계속 돌려두면 그만큼 낭비다.
            if (channel.Level <= 0f)
            {
                source.Stop();
            }
        }
    }

    // 이어지는 소리 하나. Level 은 0~1 의 페이드 진행도고, 실제 볼륨은 Ceiling 을 곱한 값이다.
    // 페이드를 볼륨에 직접 쓰지 않는 이유는, 데이터의 볼륨이 바뀌어도 페이드 진행도가 안 망가지게
    // 하려는 것이다.
    private sealed class LoopChannel
    {
        public AudioSource Source;
        public float Ceiling = 1f;
        public float Level;
        public float TargetLevel;
        public float Scale = 1f;
        public float FadeSpeed;
    }

    // 개발용 표시가 소리 상태를 들여다보는 통로.
    //
    // 클립이 비어 있어도 호출부는 조용히 넘어가도록 만들어 놨다(에셋이 준비되기 전에 호출부를
    // 미리 심을 수 있게 하려는 것). 그 대가로 "왜 소리가 안 나는지"가 안 보이므로,
    // 등록 여부와 클립 유무를 따로 물어볼 수 있어야 한다.
    public bool IsRegistered(SoundKey key)
    {
        return _soundByKey.ContainsKey(key);
    }

    public bool HasClip(SoundKey key)
    {
        return _soundByKey.TryGetValue(key, out SoundData sound) && sound.HasClip;
    }

    public bool IsLoopSound(SoundKey key)
    {
        return _soundByKey.TryGetValue(key, out SoundData sound) && sound.IsLoop;
    }

    // 지금 이 이어지는 소리가 실제로 내고 있는 볼륨. 페이드 진행도가 여기 그대로 보인다.
    public float GetLoopVolume(SoundKey key)
    {
        return _loops.TryGetValue(key, out LoopChannel channel) && channel.Source.isPlaying
            ? channel.Source.volume
            : 0f;
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
