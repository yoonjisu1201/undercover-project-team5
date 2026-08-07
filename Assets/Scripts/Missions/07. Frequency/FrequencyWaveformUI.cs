using DG.Tweening;
using UnityEngine.Serialization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("파형")]
    // 왼쪽부터 순서대로 배치된 세로 바들이다. 각 바의 높이로 파형을 표현한다.
    [SerializeField] private RectTransform[] _waveBars;

    [Header("동기화 상태")]
    [SerializeField] private TMP_Text _syncStateText;
    [SerializeField] private TMP_Text _syncHintText;

    [Header("좌표 선택")]
    // 요원 시야와 목표 좌표 사이의 각도 차이를 보여준다.
    [FormerlySerializedAs("_bearingText")]
    [SerializeField] private TMP_Text _bearingDifferenceText;
    [FormerlySerializedAs("_rotateLeftButton")]
    [SerializeField] private Button _prevZoneButton;
    [FormerlySerializedAs("_rotateRightButton")]
    [SerializeField] private Button _nextZoneButton;

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

    private void Awake()
    {
        _syncState = FindFirstObjectByType<FrequencySyncState>();
        _progressText ??= FindText("ProgressText");

        if (_syncState != null)
        {
            _syncState.OnStateChanged += Redraw;
            _syncState.OnZoneCleared += HandleZoneCleared;
        }

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

    // 버튼 OnClick에서 직접 연결한다. 세 좌표 중 몇 번째를 진행할지 고른다.
    public void OnPrevZoneButtonClick() => SelectZone(-1);

    public void OnNextZoneButtonClick() => SelectZone(1);

    // 이미 통과한 좌표는 건너뛰고 남은 좌표만 돌아가며 고른다.
    private void SelectZone(int step)
    {
        if (_syncState == null || _syncState.IsCompleted)
        {
            return;
        }

        for (int offset = 1; offset <= _syncState.ZoneCount; offset++)
        {
            int index = _syncState.StageIndex + step * offset;
            index = ((index % _syncState.ZoneCount) + _syncState.ZoneCount)
                % _syncState.ZoneCount;

            if (!_syncState.IsZoneCompleted(index))
            {
                _syncState.SubmitSelectedZone(index);
                return;
            }
        }
    }

    private void Update()
    {
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

    // 목표에서 멀면 잠잠하고, 가까워지거나 주파수가 맞아갈수록 크고 빠르게 움직인다.
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

        // 안테나를 설치하기 전에는 요원이 좌표에 얼마나 가까운지가, 설치한 뒤에는 주파수 근접도가 기준이 된다.
        float closeness = _syncState != null ? _syncState.TuneCloseness01 : 0f;
        bool placed = _syncState != null && _syncState.AntennaPlaced;
        float envelope = placed
            ? Mathf.Lerp(0.25f, 1f, closeness)
            : (_syncState != null ? _syncState.SignalStrength01 : 0f);

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

        // 미션이 끝나기 전까지는 언제든 다른 좌표로 바꿀 수 있다.
        bool placed = _syncState != null && _syncState.AntennaPlaced;
        bool done = _syncState == null || _syncState.IsCompleted;
        SetButtonUsable(_prevZoneButton, !done);
        SetButtonUsable(_nextZoneButton, !done);

        if (_syncStateText == null || _syncHintText == null)
        {
            return;
        }

        if (_syncState == null)
        {
            _syncStateText.text = "신호 없음";
            _syncHintText.text = "장비를 확인하세요";
            return;
        }

        // 좌표를 막 통과했으면 몇 초간 그 안내를 먼저 보여준다.
        if (_clearedNotice != null && !_syncState.IsCompleted)
        {
            _syncStateText.text = _clearedNotice;
            _syncHintText.text = $"남은 좌표 {_syncState.ZoneCount - _syncState.CompletedZoneCount}개";
            return;
        }

        if (_syncState.IsCompleted)
        {
            _syncStateText.text = "동기화 완료";
            _syncHintText.text = "통신이 연결되었습니다";
        }
        else if (placed && _syncState.IsOnTarget)
        {
            _syncStateText.text = "주파수 일치";
            _syncHintText.text = "이대로 유지하세요";
        }
        else if (placed)
        {
            _syncStateText.text = "안테나 설치 완료";
            _syncHintText.text = "주파수 조정을 기다립니다";
        }
        else if (signal > 0f)
        {
            _syncStateText.text = "신호 감지";
            _syncHintText.text = "목표 좌표로 더 가까이";
        }
        else
        {
            _syncStateText.text = "신호 없음";
            _syncHintText.text = "안테나 방향을 돌려 신호를 찾으세요";
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

    // 요원이 보는 방향과 목표 좌표 사이의 각도 차이를 보여준다.
    // 0에 가까울수록 지금 보는 방향이 목표를 향한다는 뜻이라, P1이 "오른쪽으로 조금" 하고 유도할 수 있다.
    private string BuildBearingLabel()
    {
        if (!_syncState.HasHolder)
        {
            return "안테나 미소지";
        }

        if (_syncState.AntennaPlaced)
        {
            return "설치 완료";
        }

        float difference = _syncState.HolderRelativeBearing;
        int degrees = Mathf.RoundToInt(Mathf.Abs(difference));

        if (degrees <= AlignedDegrees)
        {
            return "정면 0° · 직진";
        }

        // 양수면 목표가 요원의 오른쪽에 있다.
        return difference > 0f ? $"오른쪽 {degrees}°" : $"왼쪽 {degrees}°";
    }

    private void ApplyRadarMarks()
    {
        bool hasHolder = _syncState != null && _syncState.HasHolder;

        // 레이더는 목표 좌표를 위쪽에 고정해 두고 읽는다. 움직이는 건 요원 표식이다.
        if (_targetMark != null)
        {
            // 목표는 항상 위(북)를 가리킨다. 돌리지 않는다.
            _targetMark.gameObject.SetActive(hasHolder);
            _targetMark.localRotation = Quaternion.identity;
        }

        if (_playerMark != null)
        {
            _playerMark.gameObject.SetActive(hasHolder);
            if (hasHolder)
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
            _distanceText.text = _progressText != null ? "연결 완료" : BuildProgressLabel("연결 완료");
        }
        else if (!hasHolder)
        {
            _distanceText.text = "안테나 미소지";
        }
        else if (_syncState.AntennaPlaced)
        {
            float remain = Mathf.Max(0f, FrequencySyncState.RequiredHoldSeconds - _syncState.HoldSeconds);
            _distanceText.text =
                _progressText != null
                    ? $"설치 완료 · 주파수 유지 {remain:0.0}초"
                    : $"{BuildProgressLabel("설치 완료")} · 주파수 유지 {remain:0.0}초";
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
            _progressText.text = BuildProgressLabel(_syncState.IsCompleted ? "연결 완료" : null);
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

    private static void SetButtonUsable(Button button, bool usable)
    {
        if (button != null)
        {
            button.interactable = usable;
        }
    }
}
