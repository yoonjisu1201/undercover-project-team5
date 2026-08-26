using DG.Tweening;
using UnityEngine.Serialization;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;

// P1 역할의 신호 파형 화면. 세 좌표 중 어디를 진행할지 고르고, 레이더로 요원을 그 좌표까지 유도한다.
// 주파수는 이 화면에서 만질 수 없다. 요원을 목표 좌표로 보내는 것까지가 P1의 일이다.
// FM 사운드도 신호를 듣는 이 화면에서 낸다. 목표와 멀면 FM_Idle, 가까워지면 FM_Tunning, 완료되면 FM_Correct.
public sealed class FrequencyWaveformUI : MonoBehaviour
{
    // 소지자 표식이 새 각도로 돌아가는 시간이다.
    private const float MarkTweenSeconds = 0.25f;
    // 이 각도 안에 들어오면 정면으로 본다. 걸어가면서 0을 정확히 맞추긴 어렵다.
    private const int AlignedDegrees = 8;
    // 좌표 통과 안내를 띄워 두는 시간이다.
    private const float ClearedNoticeSeconds = 3f;
    // 파형 높이가 새 값으로 따라붙는 속도다. 값이 클수록 즉각 반응한다.
    private const float WaveFollowSpeed = 12f;
    // 주파수가 전혀 맞지 않을 때의 기본 진폭. 화면이 죽은 것처럼 보이지 않을 정도로만 움직인다.
    private const float IdleEnvelope = 0.25f;
    // 공유 상태를 아직 못 찾았을 때 다시 찾아보는 간격이다.
    private const float SyncStateSearchSeconds = 0.5f;

    [Header("파형")]
    // 왼쪽부터 순서대로 배치된 세로 바들이다. 각 바의 높이로 파형을 표현한다.
    [Header("현지화 문구")]
    [SerializeField] private LocalizedString _noSignal;
    [SerializeField] private LocalizedString _checkEquipment;
    [SerializeField] private LocalizedString _synced;
    [SerializeField] private LocalizedString _linkEstablished;
    [SerializeField] private LocalizedString _signalDetected;
    [SerializeField] private LocalizedString _moveCloser;
    [SerializeField] private LocalizedString _rotateAntenna;
    [SerializeField] private LocalizedString _connected;
    [SerializeField] private LocalizedString _noAntenna;

    [Tooltip("{0} 남은 좌표 수")]
    [SerializeField] private LocalizedString _zonesLeft;

    [Tooltip("{0} 목표 주파수")]
    [SerializeField] private LocalizedString _target;

    [SerializeField] private RectTransform[] _waveBars;

    [Header("동기화 상태")]
    [SerializeField] private TMP_Text _syncStateText;
    [SerializeField] private TMP_Text _syncHintText;

    [Header("좌표 선택")]
    // 요원 시야와 목표 좌표 사이의 각도 차이를 보여준다.
    [FormerlySerializedAs("_bearingText")]
    [SerializeField] private TMP_Text _bearingDifferenceText;

    [Header("레이더")]
    // 안테나를 들고 있는 요원의 표식이다. 목표를 기준으로 어느 쪽을 보고 있는지에 따라 돌아간다.
    [SerializeField] private RectTransform _playerMark;
    // 목표 좌표 표식이다. 항상 위(북)에 고정되며 회전시키지 않는다.
    [SerializeField] private RectTransform _targetMark;
    [SerializeField] private TMP_Text _progressText;
    // 존까지 남은 거리를 알려준다. P1이 이 값을 보고 요원을 유도한다.
    [SerializeField] private TMP_Text _distanceText;

    [Header("사운드")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _idleClip;
    [SerializeField] private AudioClip _tuningClip;
    [SerializeField] private AudioClip _correctClip;

    private FrequencySyncState _syncState;
    // 이 화면을 여는 HQ 콘솔은 현장 기계와 별개의 완료 상태를 갖는다.
    // 그래서 공유 상태가 완료되는 순간을 직접 보고 결과 창을 띄운다.
    private bool _completionShown;
    // 바마다 진행 중인 높이 트윈. 새 값이 오면 기존 것을 끊고 다시 시작한다.
    private Tween _markTween;
    // 바마다 현재 높이를 들고 있다가 목표 높이로 부드럽게 따라가게 한다.
    private float[] _waveHeights;
    // 좌표 통과 안내를 언제까지 띄울지와 그 문구다.
    private float _clearedNoticeUntil = -1f;
    private string _clearedNotice;
    // 공유 상태를 다시 찾아볼 시각이다.
    private float _nextSyncStateSearchTime;

    private void Awake()
    {
        _progressText ??= FindText("ProgressText");

        EnsureSyncState();
        Redraw();
    }

    // 현장 기계는 라운드 중에 스폰되므로 이 화면이 먼저 깨어나면 공유 상태를 못 찾는다.
    // Awake에서 한 번만 찾으면 그대로 null로 남아 "안테나 미소지"에서 멈추므로, 찾을 때까지 다시 확인한다.
    private void EnsureSyncState()
    {
        if (_syncState != null || Time.unscaledTime < _nextSyncStateSearchTime)
        {
            return;
        }

        _nextSyncStateSearchTime = Time.unscaledTime + SyncStateSearchSeconds;

        _syncState = FindFirstObjectByType<FrequencySyncState>(FindObjectsInactive.Include);
        if (_syncState == null)
        {
            return;
        }

        _syncState.OnStateChanged += Redraw;
        _syncState.OnZoneCleared += HandleZoneCleared;
        Redraw();
    }

    private void OnDestroy()
    {
        if (_syncState != null)
        {
            _syncState.OnStateChanged -= Redraw;
            _syncState.OnZoneCleared -= HandleZoneCleared;
        }

        // 남아 있는 트윈이 이미 사라진 RectTransform을 건드리지 않도록 끊는다.
        _markTween?.Kill();
    }

    private void Update()
    {
        EnsureSyncState();
        UpdateSound();
        RefreshWaveBars();

        // 안내 문구가 끝나는 시점에 원래 상태 문구로 되돌린다.
        if (_clearedNotice != null && Time.time > _clearedNoticeUntil)
        {
            _clearedNotice = null;
            Redraw();
        }
    }

    // 좌표를 하나 통과했을 때 안내를 띄우고 성공 사운드를 낸다.
    // 미션 전체 완료가 아니라 안테나 하나를 맞출 때마다 울린다.
    private void HandleZoneCleared(int zoneNumber)
    {
        _clearedNotice = $"{zoneNumber}번 좌표 동기화 완료";
        _clearedNoticeUntil = Time.time + ClearedNoticeSeconds;

        // 루프 중인 잡음/튜닝 소리를 끊지 않고 위에 겹쳐 낸다.
        if (_audioSource != null && _correctClip != null)
        {
            _audioSource.PlayOneShot(_correctClip);
        }

        Redraw();
    }

    // 상태에 맞는 루프 사운드를 유지하고, 완료 순간에만 성공 사운드를 한 번 낸다.
    private void UpdateSound()
    {
        if (_audioSource == null || _syncState == null)
        {
            return;
        }

        // 미션이 끝나면 루프를 멈춘다. 성공 사운드는 좌표를 통과할 때마다 이미 울렸다.
        if (_syncState.IsCompleted)
        {
            if (_audioSource.isPlaying && _audioSource.loop)
            {
                _audioSource.Stop();
            }

            return;
        }

        // 목표에 가까워졌는지로 소리를 가른다. 멀면 계속 잡음(Idle), 근처에 오면 튜닝 소리로 바뀐다.
        // 안테나를 설치하기 전에는 맞출 주파수 자체가 의미 없으므로 잡음만 낸다.
        bool near = _syncState.AntennaPlaced
            && _syncState.TargetFrequency != 0f
            && Mathf.Abs(_syncState.CurrentFrequency - _syncState.TargetFrequency) <= FrequencySyncState.NearTolerance;

        AudioClip desired = near ? _tuningClip : _idleClip;
        if (desired == null || _audioSource.clip == desired)
        {
            return;
        }

        _audioSource.clip = desired;
        _audioSource.loop = true;
        _audioSource.Play();
    }

    // 파형은 주파수를 맞춰갈수록 크고 규칙적으로 변한다. 요원이 좌표에 가까워지는 것과는 무관하다.
    private void RefreshWaveBars()
    {
        if (_waveBars == null || _waveBars.Length == 0)
        {
            return;
        }

        if (_waveHeights == null || _waveHeights.Length != _waveBars.Length)
        {
            _waveHeights = new float[_waveBars.Length];
        }

        // 진폭은 주파수 근접도만 따른다. 설치 전에는 맞출 주파수 자체가 의미가 없어 잡음만 낸다.
        // (요원과 좌표 사이의 거리를 진폭에 섞으면, 다이얼을 돌리지 않아도 파형이 커져서 오해를 준다.)
        float closeness = _syncState != null ? _syncState.TuneCloseness01 : 0f;
        bool placed = _syncState != null && _syncState.AntennaPlaced;
        float envelope = placed ? Mathf.Lerp(IdleEnvelope, 1f, closeness) : IdleEnvelope;

        float amplitude = Mathf.Lerp(0.06f, 1f, envelope);
        float speed = Mathf.Lerp(1.2f, 7f, envelope);
        // 주파수가 맞아갈수록 잡음이 걷히고 규칙적인 사인파만 남는다.
        float purity = closeness;
        float follow = 1f - Mathf.Exp(-WaveFollowSpeed * Time.deltaTime);

        for (int index = 0; index < _waveBars.Length; index++)
        {
            RectTransform bar = _waveBars[index];
            if (bar == null)
            {
                continue;
            }

            float phase = Time.time * speed + index * 0.6f;
            float wave = (Mathf.Sin(phase) + 1f) * 0.5f;
            float noise = UnityEngine.Random.value;
            float target = Mathf.Lerp(noise, wave, purity) * amplitude;

            // 목표 높이로 지수 감쇠로 따라가게 해서 계단처럼 튀지 않게 한다.
            _waveHeights[index] = Mathf.Lerp(_waveHeights[index], target, follow);
            float height = _waveHeights[index];

            bar.anchorMin = new Vector2(bar.anchorMin.x, 0.5f - height * 0.5f);
            bar.anchorMax = new Vector2(bar.anchorMax.x, 0.5f + height * 0.5f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(bar.sizeDelta.x, 0f);
        }
    }

    private void Redraw()
    {
        float signal = _syncState != null ? _syncState.SignalStrength01 : 0f;

        if (_bearingDifferenceText != null && _syncState != null)
        {
            _bearingDifferenceText.text = BuildBearingLabel();
        }

        ApplyRadarMarks();
        ShowCompletionOnce();

        bool placed = _syncState != null && _syncState.AntennaPlaced;

        if (_syncStateText == null || _syncHintText == null)
        {
            return;
        }

        if (_syncState == null)
        {
            _syncStateText.text = _noSignal.GetLocalizedString();
            _syncHintText.text = _checkEquipment.GetLocalizedString();
            return;
        }

        // 좌표를 막 통과했으면 몇 초간 그 안내를 먼저 보여준다.
        if (_clearedNotice != null && !_syncState.IsCompleted)
        {
            _syncStateText.text = _clearedNotice;
            _syncHintText.text = _zonesLeft.GetLocalizedString(_syncState.ZoneCount - _syncState.CompletedZoneCount);
            return;
        }

        if (_syncState.IsCompleted)
        {
            _syncStateText.text = _synced.GetLocalizedString();
            _syncHintText.text = _linkEstablished.GetLocalizedString();
        }
        else if (placed)
        {
            // 안테나 설치 여부는 레이더 쪽에 이미 나오므로 여기서는 반복하지 않는다.
            // 목표 주파수는 이 화면만 볼 수 있다. 현장은 현재 값만 보이므로, 여기서 방향을 읽어 전달해야 맞출 수 있다.
            _syncStateText.text = _target.GetLocalizedString(_syncState.TargetFrequency.ToString("0.00"));
            _syncHintText.text = BuildTuneGuidanceLabel();
        }
        else if (signal > 0f)
        {
            _syncStateText.text = _signalDetected.GetLocalizedString();
            _syncHintText.text = _moveCloser.GetLocalizedString();
        }
        else
        {
            _syncStateText.text = _noSignal.GetLocalizedString();
            _syncHintText.text = _rotateAntenna.GetLocalizedString();
        }
    }

    // 세 좌표를 모두 맞춘 순간 이 화면에도 결과 창을 띄운다.
    // 현장 기계가 완료를 확정하므로, 이 화면을 연 HQ 콘솔의 완료 상태만 봐서는 알 수 없다.
    private void ShowCompletionOnce()
    {
        if (_completionShown || _syncState == null || !_syncState.IsCompleted)
        {
            return;
        }

        _completionShown = true;
        GetComponent<MissionUIController>()?.ShowCompletedState();
    }

    // 현재 주파수를 목표까지 어느 쪽으로 얼마나 움직여야 하는지 알려준다.
    // 목표값은 이 화면만 알고 있어서, 본부가 이걸 읽어 현장에 전달해야 한다.
    private string BuildTuneGuidanceLabel()
    {
        float difference = _syncState.TargetFrequency - _syncState.CurrentFrequency;
        float absolute = Mathf.Abs(difference);

        // 맞춘 뒤에는 3초를 버텨야 통과되므로 남은 시간을 알려준다.
        if (absolute <= FrequencySyncState.MatchTolerance)
        {
            float remain = Mathf.Max(0f, FrequencySyncState.RequiredHoldSeconds - _syncState.HoldSeconds);
            return Localize("mission_freq_tune_matched", remain.ToString("0.0"));
        }

        if (absolute <= FrequencySyncState.NearTolerance)
        {
            return Localize("mission_freq_tune_near");
        }

        return Localize(difference > 0f ? "mission_freq_tune_high" : "mission_freq_tune_low");
    }

    // 요원이 보는 방향과 목표 좌표 사이의 각도 차이를 보여준다.
    // 0에 가까울수록 지금 보는 방향이 목표를 향한다는 뜻이라, P1이 "오른쪽으로 조금" 하고 유도할 수 있다.
    private string BuildBearingLabel()
    {
        // 설치를 마치면 안테나가 소모되어 소지자가 없어진다. 설치 완료를 미소지보다 먼저 판단해야 한다.
        if (_syncState.AntennaPlaced)
        {
            return Localize("mission_freq_antenna_placed");
        }

        if (!_syncState.HasHolder)
        {
            return _noAntenna.GetLocalizedString();
        }

        float difference = _syncState.HolderRelativeBearing;
        int degrees = Mathf.RoundToInt(Mathf.Abs(difference));

        if (degrees <= AlignedDegrees)
        {
            return Localize("mission_freq_bearing_front");
        }

        // 양수면 목표가 요원의 오른쪽에 있다.
        return Localize(difference > 0f ? "mission_freq_bearing_right" : "mission_freq_bearing_left", degrees);
    }

    private void ApplyRadarMarks()
    {
        bool hasHolder = _syncState != null && _syncState.HasHolder;
        bool placed = _syncState != null && _syncState.AntennaPlaced;

        // 설치를 마친 뒤에는 안내할 방향이 없다. 다음 좌표용 안테나를 새로 주우면 소지자가 다시 생기는데,
        // 그 사람이 몸을 돌리는 대로 표식이 계속 돌면 이미 끝난 좌표를 가리키는 잘못된 안내가 된다.
        bool showMarks = placed || hasHolder;

        // 레이더는 목표 좌표를 위쪽에 고정해 두고 읽는다. 움직이는 건 요원 표식이다.
        if (_targetMark != null)
        {
            // 목표는 항상 위(북)를 가리킨다. 돌리지 않는다.
            _targetMark.gameObject.SetActive(showMarks);
            _targetMark.localRotation = Quaternion.identity;
        }

        if (_playerMark != null)
        {
            _playerMark.gameObject.SetActive(showMarks);

            if (placed)
            {
                // 목표 표식과 겹쳐 놓고 고정한다. 도착해서 세웠다는 뜻이다.
                _markTween?.Kill();
                _markTween = null;
                _playerMark.localRotation = Quaternion.identity;
            }
            else if (hasHolder)
            {
                // 요원이 목표를 기준으로 어느 쪽을 보고 있는지. 몸을 돌리면 이 표식이 돈다.
                // 두 표식이 겹치면 목표를 정면으로 보고 있다는 뜻이라 그대로 걸어가면 된다.
                _markTween?.Kill();
                _markTween = _playerMark
                    .DOLocalRotate(new Vector3(0f, 0f, _syncState.HolderRelativeBearing), MarkTweenSeconds)
                    .SetEase(Ease.OutQuad);
            }
        }

        if (_distanceText == null)
        {
            ApplyProgressText();
            return;
        }

        ApplyProgressText();
        if (_syncState != null && _syncState.IsCompleted)
        {
            _distanceText.text = _progressText != null
                ? _connected.GetLocalizedString()
                : BuildProgressLabel(_connected.GetLocalizedString());
        }
        else if (_syncState == null)
        {
            // 아직 공유 상태를 못 찾은 것이라 소지 여부를 알 수 없다. 미소지로 단정하지 않는다.
            _distanceText.text = _noSignal.GetLocalizedString();
        }
        else if (_syncState.AntennaPlaced)
        {
            // 설치를 마치면 안테나가 소모되어 소지자가 없어진다. 미소지보다 먼저 판단해야 한다.
            float remain = Mathf.Max(0f, FrequencySyncState.RequiredHoldSeconds - _syncState.HoldSeconds);
            _distanceText.text =
                _progressText != null
                    ? $"설치 완료 · 주파수 유지 {remain:0.0}초"
                    : $"{BuildProgressLabel("설치 완료")} · 주파수 유지 {remain:0.0}초";
        }
        else if (!hasHolder)
        {
            _distanceText.text = _noAntenna.GetLocalizedString();
        }
        else
        {
            _distanceText.text =
                _progressText != null
                    ? $"남은 거리 {_syncState.HolderDistance:0.0}m"
                    : $"{BuildProgressLabel()} · 남은 거리 {_syncState.HolderDistance:0.0}m";
        }
    }

    private void ApplyProgressText()
    {
        if (_progressText != null && _syncState != null)
        {
            _progressText.text = BuildProgressLabel(_syncState.IsCompleted ? _connected.GetLocalizedString() : null);
        }
    }

    private string BuildProgressLabel(string suffix = null)
    {
        string progress = $"{_syncState.CompletedZoneCount} / {_syncState.ZoneCount}";
        return string.IsNullOrEmpty(suffix) ? progress : $"{progress} {suffix}";
    }

    private TMP_Text FindText(string childName)
    {
        foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.name == childName)
            {
                return text;
            }
        }

        return null;
    }

    private static string Localize(string key, params object[] args)
    {
        var localized = new UnityEngine.Localization.LocalizedString("Language Table", key);
        return args == null || args.Length == 0
            ? localized.GetLocalizedString()
            : localized.GetLocalizedString(args);
    }
}
