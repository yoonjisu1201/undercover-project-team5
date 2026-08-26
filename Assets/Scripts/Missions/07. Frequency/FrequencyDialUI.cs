using TMPro;
using UnityEngine.Serialization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

// P3 역할의 주파수 다이얼 화면. 안테나가 존에 자리 잡기 전에는 잠겨 있고, 열린 뒤 목표 주파수를 맞추면 즉시 완료된다.
// FM 사운드는 신호를 듣는 쪽인 P1(FrequencyWaveformUI)에서 재생한다.
public sealed class FrequencyDialUI : MonoBehaviour
{
    // 노브가 도는 각도 범위다. 한 바퀴 넘게 돌려야 끝에서 끝까지 가므로 빙글빙글 돌리는 느낌이 난다.
    private const float KnobSweepDegrees = 1080f;
    // 롤러를 이만큼 끌 때마다 주파수 한 칸이 움직인다. 작을수록 조금만 끌어도 많이 움직인다.
    private const float RollerPixelsPerStep = 4f;
    // 롤러 이랑무늬 한 칸의 폭이다. 이 값으로 나눈 나머지만 움직여 무늬가 끝없이 이어져 보이게 한다.
    private const float RollerTileWidth = 32f;

    [Header("조작 방식")]
    // 0 = 노브, 1 = 슬라이더, 2 = 롤러. ◀▶ 버튼으로 골라 쓴다.
    [Header("현지화 문구")]
    [SerializeField] private LocalizedString _hint;

    [SerializeField] private GameObject[] _controlRoots;
    [SerializeField] private Button _prevControlButton;
    [SerializeField] private Button _nextControlButton;
    [SerializeField] private TMP_Text _controlNameText;

    [Header("노브")]
    [SerializeField] private FrequencyDialKnob _knob;
    // 노브 스프라이트와 눈금 표시를 함께 돌리는 회전 대상이다.
    [SerializeField] private RectTransform _knobVisual;

    [Header("슬라이더")]
    // 유니티 내장 Slider를 그대로 쓴다. 범위는 프리팹에서 100~115MHz로 잡아둔다.
    [SerializeField] private Slider _frequencySlider;

    [Header("롤러")]
    [SerializeField] private FrequencyDragArea _rollerDrag;
    // 가로로 끌면 흘러가는 이랑무늬다.
    [SerializeField] private RectTransform _rollerRibs;

    [Header("표시")]
    [SerializeField] private TMP_Text _progressText;
    [SerializeField] private TMP_Text _currentFrequencyText;
    [SerializeField] private TMP_Text _targetFrequencyText;
    [SerializeField] private TMP_Text _hintText;
    // 100.00~115.00 눈금 위에서 현재 주파수 위치를 보여주는 핸들이다.
    [SerializeField] private RectTransform _sliderHandle;
    [SerializeField] private RectTransform _sliderTrack;

    [Header("안내 창")]
    // 좌표 통과·안테나 미설치를 알리는 작은 창이다. 평소에는 닫혀 있고 필요할 때만 띄운다.
    [FormerlySerializedAs("_lockOverlay")]
    [SerializeField] private GameObject _noticePanel;
    [FormerlySerializedAs("_lockText")]
    [SerializeField] private TMP_Text _noticeText;

    // 마지막 조작 후 이 시간 동안은 서버가 보내온 값을 무시한다.
    // 돌리는 중에 한 박자 늦은 서버 값이 덮어쓰면 노브가 앞뒤로 튕겨 끊기는 것처럼 보인다.
    private const float LocalControlHoldSeconds = 0.5f;
    // 공유 상태를 아직 못 찾았을 때 다시 찾아보는 간격이다.
    private const float SyncStateSearchSeconds = 0.5f;

    private FrequencySyncState _syncState;
    // 공유 상태를 다시 찾아볼 시각이다.
    private float _nextSyncStateSearchTime;
    // 서버 응답을 기다리지 않고 손맛을 유지하기 위해 이 화면이 들고 있는 값이다.
    private float _localFrequency;
    private float _lastLocalTuneTime = -99f;
    private int _controlIndex;
    // 좌표가 바뀌면 그 번호에 맞는 조작으로 옮기기 위해 직전 좌표를 기억한다.
    private int _lastStageIndex = -1;

    private void Awake()
    {
        _localFrequency = FrequencySyncState.MinFrequency;
        _progressText ??= FindText("ProgressText");

        EnsureSyncState();

        if (_knob != null)
        {
            _knob.OnRotated += HandleKnobRotated;
        }

        if (_rollerDrag != null)
        {
            _rollerDrag.OnDelta += HandleRollerDelta;
        }

        Redraw();
    }

    private void Update()
    {
        EnsureSyncState();
    }

    // 현장 기계는 라운드 중에 스폰되므로 이 화면이 먼저 깨어나면 공유 상태를 못 찾는다.
    // Awake에서 한 번만 찾으면 그대로 null로 남아 다이얼이 영원히 잠긴 것처럼 보이므로, 찾을 때까지 다시 확인한다.
    // 이 미션은 라운드당 하나만 존재하므로 찾은 하나를 그대로 구독한다.
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

        _localFrequency = _syncState.CurrentFrequency;
        _syncState.OnStateChanged += HandleStateChanged;
        _syncState.OnZoneCleared += HandleZoneCleared;
        Redraw();
    }

    private void OnDestroy()
    {
        if (_syncState != null)
        {
            _syncState.OnStateChanged -= HandleStateChanged;
            _syncState.OnZoneCleared -= HandleZoneCleared;
        }

        if (_knob != null)
        {
            _knob.OnRotated -= HandleKnobRotated;
        }

        if (_rollerDrag != null)
        {
            _rollerDrag.OnDelta -= HandleRollerDelta;
        }
    }

    // 노브를 DegreesPerStep만큼 돌릴 때마다 주파수가 한 칸(0.05MHz) 움직인다.
    private void HandleKnobRotated(float degrees)
    {
        Tune(degrees / FrequencyDialKnob.DegreesPerStep * FrequencySyncState.FrequencyStep);
    }

    // 슬라이더 OnValueChanged에서 직접 연결한다. 값이 곧 주파수(MHz)다.
    public void OnFrequencySliderChanged(float value)
    {
        Tune(value - _localFrequency);
    }

    // 롤러는 끌어당긴 거리만큼 주파수가 흘러간다. 오른쪽으로 끌면 올라간다.
    private void HandleRollerDelta(float pixels)
    {
        Tune(pixels / RollerPixelsPerStep * FrequencySyncState.FrequencyStep);
    }

    // 좌표를 하나 통과하면 안내 창을 띄운다. 확인을 눌러야 닫힌다.
    private void HandleZoneCleared(int zoneNumber)
    {
        int remaining = _syncState != null ? _syncState.ZoneCount - _syncState.CompletedZoneCount : 0;
        ShowNotice(remaining > 0
            ? $"{zoneNumber}번 좌표 동기화 완료\n남은 좌표 {remaining}개"
            : $"{zoneNumber}번 좌표 동기화 완료");
    }

    // 안내 창을 띄운다. 화면 전체를 덮지 않고 창만 올린다.
    private void ShowNotice(string message)
    {
        if (_noticeText != null)
        {
            _noticeText.text = message;
        }

        _noticePanel?.SetActive(true);
    }

    // 안내 창의 확인 버튼에서 직접 연결한다.
    public void OnNoticeConfirmButtonClick()
    {
        _noticePanel?.SetActive(false);
    }

    // 버튼 OnClick에서 직접 연결한다.
    public void OnPrevControlButtonClick() => SwitchControl(-1);

    public void OnNextControlButtonClick() => SwitchControl(1);

    // ◀▶ 버튼으로 노브·슬라이더·롤러를 돌려가며 고른다.
    private void SwitchControl(int step)
    {
        if (_controlRoots == null || _controlRoots.Length == 0)
        {
            return;
        }

        _controlIndex = (_controlIndex + step + _controlRoots.Length) % _controlRoots.Length;
        Redraw();
    }

    private void Tune(float amount)
    {
        if (_syncState == null)
        {
            return;
        }

        // 안테나가 설치되지 않았는데 만지려 하면 이유를 알려준다.
        if (!_syncState.DialUnlocked)
        {
            if (!_syncState.IsCompleted)
            {
                ShowNotice("안테나를 설치해야 합니다\n요원이 목표 좌표에 안테나를 세워야 조작할 수 있습니다");
            }

            return;
        }

        // 눈금 밖의 값이 생기지 않도록 항상 0.05MHz 배수로 맞춘다. 목표도 같은 배수라 반드시 도달할 수 있다.
        float tuned = FrequencySyncState.SnapFrequency(_localFrequency + amount);
        if (Mathf.Approximately(tuned, _localFrequency))
        {
            return;
        }

        _localFrequency = tuned;
        _lastLocalTuneTime = Time.time;
        _syncState.SubmitFrequency(_localFrequency);
        Redraw();
    }

    // 서버 값이 바뀌면 내 예측값을 서버 값으로 맞춘다. 다른 사람이 이어서 돌린 경우도 이 경로로 반영된다.
    private void HandleStateChanged()
    {
        // 내가 방금 돌린 직후라면 서버 값으로 되돌리지 않는다. 되돌리면 노브가 튕겨 끊긴다.
        bool controllingNow = Time.time - _lastLocalTuneTime <= LocalControlHoldSeconds;
        if (_syncState != null && !controllingNow)
        {
            _localFrequency = _syncState.CurrentFrequency;
        }

        Redraw();
    }

    private void Redraw()
    {
        bool unlocked = _syncState != null && _syncState.DialUnlocked;

        // 첫 좌표는 노브, 두 번째는 슬라이더, 세 번째는 롤러를 쓰게 한다.
        // 좌표가 바뀌는 순간에만 옮기므로, 그 뒤에 버튼으로 다른 조작을 골라도 유지된다.
        if (_syncState != null && _controlRoots != null && _controlRoots.Length > 0
            && _syncState.StageIndex != _lastStageIndex)
        {
            _lastStageIndex = _syncState.StageIndex;
            _controlIndex = Mathf.Clamp(_syncState.StageIndex, 0, _controlRoots.Length - 1);
        }

        // 조작을 막아 버리면 입력이 들어오지 않아 안내를 띄울 수 없다.
        // 그래서 입력은 항상 받고, 실제 반영 여부는 Tune에서 판단한다.
        bool operable = _syncState == null || !_syncState.IsCompleted;
        if (_knob != null)
        {
            _knob.Interactable = operable;
        }

        if (_frequencySlider != null)
        {
            _frequencySlider.interactable = operable;
        }

        if (_rollerDrag != null)
        {
            _rollerDrag.Interactable = operable;
        }

        ApplyControlSelection();

        if (_currentFrequencyText != null)
        {
            _currentFrequencyText.text = $"{_localFrequency:0.00}";
        }

        if (_targetFrequencyText != null)
        {
            // 목표 주파수는 본부 화면에만 나온다. 여기서 보여주면 본부에 물어볼 이유가 없어진다.
            _targetFrequencyText.text = _syncState != null && _syncState.IsCompleted
                ? $"{_syncState.TargetFrequency:0.00} MHz"
                : "??? MHz";
        }

        ApplyKnobRotation();
        ApplySliderHandle();
        ApplySliderControl();
        ApplyRoller();
        ApplyProgressText();
        ApplyHintText(unlocked);
    }

    // 고른 조작만 켜고 나머지는 끈다.
    private void ApplyControlSelection()
    {
        if (_controlRoots == null)
        {
            return;
        }

        for (int index = 0; index < _controlRoots.Length; index++)
        {
            if (_controlRoots[index] != null)
            {
                _controlRoots[index].SetActive(index == _controlIndex);
            }
        }

        if (_controlNameText != null)
        {
            _controlNameText.text = _controlIndex switch
            {
                0 => "노브",
                1 => "슬라이더",
                _ => "롤러"
            };
        }
    }

    // 슬라이더 손잡이를 현재 주파수에 맞춘다. onValueChanged가 다시 불려 되먹임이 생기지 않도록 알림 없이 넣는다.
    private void ApplySliderControl()
    {
        if (_frequencySlider != null)
        {
            _frequencySlider.SetValueWithoutNotify(_localFrequency);
        }
    }

    // 롤러 무늬를 흘려 보낸다. 한 칸 폭으로 나눈 나머지만 쓰므로 끝없이 이어지는 것처럼 보인다.
    private void ApplyRoller()
    {
        if (_rollerRibs == null)
        {
            return;
        }

        float steps = (_localFrequency - FrequencySyncState.MinFrequency) / FrequencySyncState.FrequencyStep;
        float offset = -steps * RollerPixelsPerStep % RollerTileWidth;
        _rollerRibs.anchoredPosition = new Vector2(offset, 0f);
    }

    // 주파수 범위를 노브의 회전 범위로 환산한다. 시계 방향으로 돌 때 주파수가 올라가도록 음수 Z를 쓴다.
    private void ApplyKnobRotation()
    {
        if (_knobVisual == null)
        {
            return;
        }

        float ratio = Mathf.InverseLerp(
            FrequencySyncState.MinFrequency, FrequencySyncState.MaxFrequency, _localFrequency);
        _knobVisual.localRotation = Quaternion.Euler(0f, 0f, KnobSweepDegrees * 0.5f - ratio * KnobSweepDegrees);
    }

    private void ApplySliderHandle()
    {
        if (_sliderHandle == null || _sliderTrack == null)
        {
            return;
        }

        float ratio = Mathf.InverseLerp(
            FrequencySyncState.MinFrequency, FrequencySyncState.MaxFrequency, _localFrequency);
        _sliderHandle.anchorMin = new Vector2(ratio, 0f);
        _sliderHandle.anchorMax = new Vector2(ratio, 1f);
        _sliderHandle.anchoredPosition = Vector2.zero;
    }

    // 목표에 얼마나 가까운지는 알려주지 않는다. 그걸 여기서 보여주면 본부에 물어볼 이유가 없어져
    // 혼자 다이얼만 돌려도 미션이 끝난다. 근접도와 방향은 본부 화면(FrequencyWaveformUI)에만 나온다.
    private void ApplyHintText(bool unlocked)
    {
        if (_hintText == null)
        {
            return;
        }

        if (_syncState == null || !unlocked)
        {
            _hintText.text = _syncState != null && _syncState.IsCompleted
                ? "동기화 완료"
                : "다이얼 잠김\n요원이 목표 좌표에 도착해야 합니다";
            return;
        }

        _hintText.text = _hint.GetLocalizedString();
    }

    private void ApplyProgressText()
    {
        if (_progressText != null && _syncState != null)
        {
            _progressText.text = $"{_syncState.CompletedZoneCount} / {_syncState.ZoneCount}";
        }
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
}
