using TMPro;
using UnityEngine;
using UnityEngine.UI;

// P3 역할의 주파수 다이얼 화면. 안테나가 존에 자리 잡기 전에는 잠겨 있고, 열린 뒤 목표 주파수를 맞추면 즉시 완료된다.
// FM 사운드는 신호를 듣는 쪽인 P1(FrequencyWaveformUI)에서 재생한다.
public sealed class FrequencyDialUI : MonoBehaviour
{
    // 노브가 실제로 돌 수 있는 각도 범위다. 최소 주파수에서 -135도, 최대에서 +135도를 보게 한다.
    private const float KnobSweepDegrees = 270f;
    // 롤러를 이만큼 끌 때마다 주파수 한 칸이 움직인다.
    private const float RollerPixelsPerStep = 10f;
    // 롤러 이랑무늬 한 칸의 폭이다. 이 값으로 나눈 나머지만 움직여 무늬가 끝없이 이어져 보이게 한다.
    private const float RollerTileWidth = 32f;

    [Header("조작 방식")]
    // 0 = 노브, 1 = 슬라이더, 2 = 롤러. ◀▶ 버튼으로 골라 쓴다.
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
    [SerializeField] private TMP_Text _currentFrequencyText;
    [SerializeField] private TMP_Text _targetFrequencyText;
    [SerializeField] private TMP_Text _hintText;
    // 100.00~115.00 눈금 위에서 현재 주파수 위치를 보여주는 핸들이다.
    [SerializeField] private RectTransform _sliderHandle;
    [SerializeField] private RectTransform _sliderTrack;

    [Header("잠금")]
    // 안테나가 아직 존에 없을 때 다이얼을 덮는다.
    [SerializeField] private GameObject _lockOverlay;
    [SerializeField] private TMP_Text _lockText;

    private FrequencySyncState _syncState;
    // 서버 응답을 기다리지 않고 손맛을 유지하기 위해 이 화면이 들고 있는 값이다.
    private float _localFrequency;
    private int _controlIndex;

    private void Awake()
    {
        // 이 미니게임은 라운드당 하나만 존재하므로 다른 오브젝트에 있는 공유 상태를 찾아 구독한다.
        _syncState = FindFirstObjectByType<FrequencySyncState>();
        _localFrequency = _syncState != null ? _syncState.CurrentFrequency : FrequencySyncState.MinFrequency;

        if (_syncState != null)
        {
            _syncState.OnStateChanged += HandleStateChanged;
        }

        if (_knob != null)
        {
            _knob.OnRotated += HandleKnobRotated;
        }

        if (_frequencySlider != null)
        {
            _frequencySlider.onValueChanged.AddListener(HandleSliderValue);
        }

        if (_rollerDrag != null)
        {
            _rollerDrag.OnDelta += HandleRollerDelta;
        }

        _prevControlButton?.onClick.AddListener(() => SwitchControl(-1));
        _nextControlButton?.onClick.AddListener(() => SwitchControl(1));

        Redraw();
    }

    private void OnDestroy()
    {
        if (_syncState != null)
        {
            _syncState.OnStateChanged -= HandleStateChanged;
        }

        if (_knob != null)
        {
            _knob.OnRotated -= HandleKnobRotated;
        }

        if (_frequencySlider != null)
        {
            _frequencySlider.onValueChanged.RemoveListener(HandleSliderValue);
        }

        if (_rollerDrag != null)
        {
            _rollerDrag.OnDelta -= HandleRollerDelta;
        }
    }

    // 노브를 DegreesPerStep만큼 돌릴 때마다 주파수가 한 칸(0.05MHz) 움직인다.
    private void HandleKnobRotated(float degrees)
    {
        Tune(degrees / FrequencyDialKnob.DegreesPerStep * FrequencySyncState.FrequencyTolerance);
    }

    // 내장 Slider의 값은 곧 주파수(MHz)다. 지금 값과의 차이만큼 움직인다.
    private void HandleSliderValue(float value)
    {
        Tune(value - _localFrequency);
    }

    // 롤러는 끌어당긴 거리만큼 주파수가 흘러간다. 오른쪽으로 끌면 올라간다.
    private void HandleRollerDelta(float pixels)
    {
        Tune(pixels / RollerPixelsPerStep * FrequencySyncState.FrequencyTolerance);
    }

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
        if (_syncState == null || !_syncState.DialUnlocked)
        {
            return;
        }

        float tuned = Mathf.Clamp(
            _localFrequency + amount,
            FrequencySyncState.MinFrequency,
            FrequencySyncState.MaxFrequency);

        // 눈금 밖의 값이 생기지 않도록 항상 0.05MHz 배수로 맞춘다. 목표도 같은 배수라 반드시 도달할 수 있다.
        tuned = Mathf.Round(tuned / FrequencySyncState.FrequencyTolerance) * FrequencySyncState.FrequencyTolerance;
        if (Mathf.Approximately(tuned, _localFrequency))
        {
            return;
        }

        _localFrequency = tuned;
        _syncState.SubmitFrequency(_localFrequency);
        Redraw();
    }

    // 서버 값이 바뀌면 내 예측값을 서버 값으로 맞춘다. 다른 사람이 이어서 돌린 경우도 이 경로로 반영된다.
    private void HandleStateChanged()
    {
        if (_syncState != null)
        {
            _localFrequency = _syncState.CurrentFrequency;
        }

        Redraw();
    }

    private void Redraw()
    {
        bool unlocked = _syncState != null && _syncState.DialUnlocked;

        if (_knob != null)
        {
            _knob.Interactable = unlocked;
        }

        if (_frequencySlider != null)
        {
            _frequencySlider.interactable = unlocked;
        }

        if (_rollerDrag != null)
        {
            _rollerDrag.Interactable = unlocked;
        }

        ApplyControlSelection();

        if (_lockOverlay != null)
        {
            _lockOverlay.SetActive(_syncState != null && !_syncState.AntennaPlaced);
        }

        if (_lockText != null)
        {
            _lockText.text = "안테나 설치 대기 중\n요원이 목표 지점에 도착해야 합니다";
        }

        if (_currentFrequencyText != null)
        {
            _currentFrequencyText.text = $"{_localFrequency:0.00}";
        }

        if (_targetFrequencyText != null && _syncState != null)
        {
            _targetFrequencyText.text = $"{_syncState.TargetFrequency:0.00} MHz";
        }

        ApplyKnobRotation();
        ApplySliderHandle();
        ApplySliderControl();
        ApplyRoller();
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

        float steps = (_localFrequency - FrequencySyncState.MinFrequency) / FrequencySyncState.FrequencyTolerance;
        float offset = -steps * RollerPixelsPerStep % RollerTileWidth;
        _rollerRibs.anchoredPosition = new Vector2(offset, 0f);
    }

    private static void SetButtonUsable(Button button, bool usable)
    {
        if (button != null)
        {
            button.interactable = usable;
        }
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

    // 목표까지의 거리만 알려준다. 어느 쪽으로 돌려야 하는지는 알려주지 않아 다이얼을 직접 훑어야 한다.
    private void ApplyHintText(bool unlocked)
    {
        if (_hintText == null)
        {
            return;
        }

        if (_syncState == null || !unlocked)
        {
            _hintText.text = _syncState != null && _syncState.IsCompleted ? "동기화 완료" : "다이얼 잠김";
            return;
        }

        float error = Mathf.Abs(_localFrequency - _syncState.TargetFrequency);
        _hintText.text = error switch
        {
            <= FrequencySyncState.FrequencyTolerance => "주파수 일치\n통신이 연결되었습니다",
            <= 0.5f => "목표 주파수 근처\n미세 조정이 필요합니다",
            <= 2f => "목표 주파수에 접근 중",
            _ => "신호가 잡히지 않습니다"
        };
    }
}
