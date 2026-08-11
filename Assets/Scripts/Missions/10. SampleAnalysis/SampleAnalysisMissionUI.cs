using System;
using DG.Tweening;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 샘플 분석 장치의 화면이다. 세 역할이 같은 프리팹을 쓰고, 고른 자리에 따라 보여줄 패널만 바뀐다.
// P1(관찰 모니터) : 수치가 안 보인다. 좌우 상태 패널과 파형만 보고 P2, P3에게 방향을 말해준다.
// P2(온도 조절)   : 온도 수치와 ▲▼ 버튼만 있다. 정답 범위는 모른다.
// P3(농도 조절)   : 농도 쪽도 같다.
// 자리는 서버가 관리하므로 이미 누가 앉은 자리는 회색으로 잠긴다.
public sealed class SampleAnalysisMissionUI : MonoBehaviour
{
    [Header("패널 페이드 시간")]
    // 패널을 바꿀 때의 페이드 시간이다. 길면 조작이 느리게 느껴진다.
    private const float PanelFadeSeconds = 0.12f;
    // 세포 붕괴 단계에서 파형이 깜빡이는 시간이다.
    private const float WaveformFadeSeconds = 0.18f;
    // 이 이름으로 시작하는 자식은 막대형으로, 나머지는 선분형으로 움직인다.
    private const string WaveBarNamePrefix = "WaveBar";

    private const string ObserverName = "관찰 모니터";
    private const string TemperatureName = "온도 조절";
    private const string ConcentrationName = "농도 조절";

    // 비어 있는 자리는 초록, 사용 중인 자리는 회색으로 칠한다.
    private static readonly Color AvailableRoleColor = new(0.08f, 0.58f, 0.34f, 1f);    // Green
    private static readonly Color OccupiedRoleColor = new(0.26f, 0.29f, 0.29f, 1f);     // Gray

    // 버튼 갱신은 항상 세 자리를 함께 돌린다.
    private static readonly SampleAnalysisRole[] AllRoles =
    {
        SampleAnalysisRole.Observer,
        SampleAnalysisRole.Temperature,
        SampleAnalysisRole.Concentration
    };

    // 네트워크가 돌지 않는 단독 실행에서는 0번으로 본다.
    private static ulong LocalClientId =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.LocalClientId : 0UL;

    [Header("패널")]
    [SerializeField] private GameObject _roleSelectPanel;
    [SerializeField] private GameObject _observerPanel;
    [SerializeField] private GameObject _temperaturePanel;
    [SerializeField] private GameObject _concentrationPanel;
    [SerializeField] private GameObject _roleChangeBar;

    [Header("역할별 화면 참조")]
    [SerializeField] private RolePanelView _observerView = new();
    [SerializeField] private RolePanelView _temperatureView = new();
    [SerializeField] private RolePanelView _concentrationView = new();

    [Header("처음 역할 선택 버튼")]
    [SerializeField] private Button _initialObserverButton;
    [SerializeField] private Button _initialTemperatureButton;
    [SerializeField] private Button _initialConcentrationButton;
    [SerializeField] private TMP_Text _initialObserverSlotText;
    [SerializeField] private TMP_Text _initialTemperatureSlotText;
    [SerializeField] private TMP_Text _initialConcentrationSlotText;

    [Header("상단 역할 변경 버튼")]
    // 역할 변경 버튼은 공통 바 하나만 쓴다. 각 패널이 따로 들고 있으면 세 벌을 같이 갱신해야 한다.
    [SerializeField] private Button _observerButton;
    [SerializeField] private Button _temperatureButton;
    [SerializeField] private Button _concentrationButton;
    [SerializeField] private TMP_Text _observerLabel;
    [SerializeField] private TMP_Text _temperatureLabel;
    [SerializeField] private TMP_Text _concentrationLabel;

    [Header("관찰 모니터 전용 상태")]
    // 관찰 모니터에만 있는 좌우 상태 패널이다. 온도·농도 패널에는 없다.
    [SerializeField] private TMP_Text _temperatureStatusText;
    [SerializeField] private TMP_Text _concentrationStatusText;
    [SerializeField] private TMP_Text _temperatureActivationText;
    [SerializeField] private TMP_Text _concentrationActivationText;
    [SerializeField] private Image _temperatureStatusLamp;
    [SerializeField] private Image _concentrationStatusLamp;



    private SampleAnalysisState _state;
    // 이 플레이어가 앉아 있는 자리다. 화면을 닫을 때 반드시 비워 줘야 다음 사람이 앉을 수 있다.
    private SampleAnalysisRole _currentRole;    // 현재 플레이어가 맡은 자리. 비어 있으면 None
    private bool _hasRole;  // 본부 필드의 역할이 아닙니다, 미션에서 맡은 역할입니다(관찰 모니터, 온도 조절, 농도 조절)
    private bool _completionShown;

    private void Awake()
    {
        // 이 미션은 라운드당 하나만 존재하므로 다른 오브젝트에 있는 공유 상태를 찾아 구독한다.
        _state = FindFirstObjectByType<SampleAnalysisState>();

        if (_state != null)
        {
            _state.OnStateChanged += Redraw;
        }

        // 이미 끝난 장치를 다시 열었을 때는 MissionInteractable이 결과 창을 띄워 준다.
        // 여기서 같이 처리하면 이 화면에서 푼 것으로 기록돼, 닫을 때 남의 완료를 내 이름으로 요청하게 된다.
        _completionShown = _state != null && _state.IsCompleted;

        // 역할 변경 바를 포함한 화면 상태는 Redraw가 _hasRole을 보고 정한다.
        Redraw();
    }

    private void OnDestroy()
    {
        if (_state != null)
        {
            _state.OnStateChanged -= Redraw;
        }

        ReleaseCurrentRole();
        KillWaveformTweens();
    }

    private void OnDisable()
    {
        // 화면이 꺼져도 DOTween은 계속 돌기 때문에 직접 끊어 준다.
        KillWaveformTweens();
    }

    // 버튼 OnClick에서 직접 연결한다. 위 세 개는 자리 선택, 아래 두 개는 값 조절이다.
    public void OnObserverButtonClick() => SelectRole(SampleAnalysisRole.Observer);

    public void OnTemperatureButtonClick() => SelectRole(SampleAnalysisRole.Temperature);

    public void OnConcentrationButtonClick() => SelectRole(SampleAnalysisRole.Concentration);

    public void OnDecreaseButtonClick() => AdjustValue(-1f);

    public void OnIncreaseButtonClick() => AdjustValue(1f);

    // 일반 닫기와 결과 확인 모두 자리를 비우고 공통 미션 UI를 닫는다.
    public void OnCloseButtonClick() => CloseUI();

    public void OnConfirmButtonClick() => CloseUI();

    // 비어 있는 자리를 골라 그 패널로 넘어간다.
    private void SelectRole(SampleAnalysisRole role)
    {
        if (_state == null || !_state.IsRoleAvailable(role, LocalClientId))
        {
            return;
        }

        // 자리를 옮기는 경우이므로 이전 자리를 먼저 비운다. 그러지 않으면 한 사람이 두 자리를 차지한 것처럼 보인다.
        ReleaseCurrentRole();

        _currentRole = role;
        _hasRole = true;
        _state.RequestRole(role);

        ShowRolePanel(role);
        Redraw();
    }

    // 지금 맡은 자리에 해당하는 값만 조절한다. 관찰 담당에게는 조절 버튼이 없다.
    private void AdjustValue(float direction)
    {
        if (_state == null || !_hasRole)
        {
            return;
        }

        if (_currentRole == SampleAnalysisRole.Temperature)
        {
            _state.AdjustTemperature(direction);
        }
        else if (_currentRole == SampleAnalysisRole.Concentration)
        {
            _state.AdjustConcentration(direction);
        }
    }

    private void CloseUI()
    {
        ReleaseCurrentRole();
        GetComponent<MissionUIController>()?.OnCloseButtonClick();
    }

    private void ReleaseCurrentRole()
    {
        if (_hasRole && _state != null)
        {
            _state.ReleaseRole(_currentRole);
        }

        _hasRole = false;
    }

    // 고른 자리의 패널만 켠다. 자리 선택 화면은 페이드 없이 바로 닫는다.
    private void ShowRolePanel(SampleAnalysisRole role)
    {
        SetPanelVisible(_roleSelectPanel, false, false);
        SetPanelVisible(_observerPanel, role == SampleAnalysisRole.Observer, true);
        SetPanelVisible(_temperaturePanel, role == SampleAnalysisRole.Temperature, true);
        SetPanelVisible(_concentrationPanel, role == SampleAnalysisRole.Concentration, true);
    }

    private void Redraw()
    {
        // 역할 변경 바는 자리를 맡았을 때만 띄운다. 자리 선택 화면에서는 고를 자리가 이미 화면에 있어 필요 없다.
        // Awake에서 한 번만 끄면 화면을 닫았다 다시 열었을 때(인스턴스 재사용) 상태가 어긋난다.
        if (_roleChangeBar != null)
        {
            _roleChangeBar.SetActive(_hasRole);
        }

        ApplyRoleButtons();
        ShowCompletionOnce();

        if (_state == null || !_hasRole)
        {
            return;
        }

        RolePanelView view = GetView(_currentRole);
        int temperatureDirection = _state.TemperatureDirection;
        int concentrationDirection = _state.ConcentrationDirection;

        ApplyProgress(view);
        ApplyValueText(view, temperatureDirection, concentrationDirection);

        // 좌우 상태 패널은 관찰 모니터 화면에만 있다.
        if (_currentRole == SampleAnalysisRole.Observer)
        {
            ApplyObserverSignals(temperatureDirection, concentrationDirection);
        }

        view.PlayWaveform(_state.ReactionLevel, temperatureDirection, concentrationDirection);
    }

    // 세 자리의 비어 있음/사용 중 표시를 갱신한다. 내가 앉아 있는 자리도 다시 고를 필요가 없어 잠근다.
    private void ApplyRoleButtons()
    {
        foreach (SampleAnalysisRole role in AllRoles)
        {
            bool empty = _state == null || _state.GetAssignedClientId(role) == SampleAnalysisState.EmptyClientId;
            bool usable = empty && (!_hasRole || _currentRole != role);
            string roleName = GetRoleName(role);

            // 처음 선택 화면의 버튼은 세로로 넓어서 문구를 두 줄로 쓴다.
            ApplyRoleButton(GetInitialButton(role), GetInitialLabel(role), roleName, usable, true);
            ApplyRoleButton(GetRoleChangeButton(role), GetRoleChangeLabel(role), roleName, usable);
        }
    }

    private void ApplyRoleButton(Button button, TMP_Text label, string roleName, bool usable, bool multiline = false)
    {
        if (button == null || label == null)
        {
            return;
        }

        button.interactable = usable;
        label.text = $"{roleName}{(multiline ? "\n" : "  |  ")}{(usable ? "비어 있음" : "사용 중")}";

        if (!button.TryGetComponent(out Image image))
        {
            return;
        }

        image.color = usable ? AvailableRoleColor : OccupiedRoleColor;

        // Button이 색을 덧칠하면 위에서 넣은 초록/회색이 지워지므로 상태 색을 흰색으로 고정한다.
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.disabledColor = Color.white;
        button.colors = colors;
    }

    private void ApplyProgress(RolePanelView view)
    {
        // 필요 시간이 0으로 설정돼도 0으로 나누지 않게 최소값을 둔다.
        float required = Mathf.Max(0.01f, _state.RequiredStableSeconds);

        if (view.StableProgress != null)
        {
            view.StableProgress.value = _state.StableSeconds / required;
        }

        if (view.ProgressText != null)
        {
            view.ProgressText.text = $"{_state.StableSeconds:0.0} / {_state.RequiredStableSeconds:0.#}초";
        }
    }

    // 조절 담당에게만 실제 수치를 보여준다. 관찰 담당이 수치까지 보면 혼자 다 읽어버려 셋이 말을 맞출 이유가 없어진다.
    private void ApplyValueText(RolePanelView view, int temperatureDirection, int concentrationDirection)
    {
        if (view.ValueText == null)
        {
            return;
        }

        if (_currentRole == SampleAnalysisRole.Temperature)
        {
            view.ValueText.text = $"{_state.Temperature:0}°C";
        }
        else if (_currentRole == SampleAnalysisRole.Concentration)
        {
            view.ValueText.text = $"{_state.Concentration:0}%";
        }
        else if (_state.IsStable)
        {
            view.ValueText.text = "반응 안정\n현재 값을 유지하세요";
        }
        else
        {
            view.ValueText.text =
                $"온도 {GetDirectionText(temperatureDirection)}\n농도 {GetDirectionText(concentrationDirection)}";
        }
    }

    private void ApplyObserverSignals(int temperatureDirection, int concentrationDirection)
    {
        SetText(_temperatureStatusText, GetTemperatureStatusText(temperatureDirection));
        SetText(_concentrationStatusText, GetConcentrationStatusText(concentrationDirection));
        SetText(_temperatureActivationText, GetActivationText(temperatureDirection));
        SetText(_concentrationActivationText, GetActivationText(concentrationDirection));
        SetColor(_temperatureStatusLamp, GetSignalColor(temperatureDirection));
        SetColor(_concentrationStatusLamp, GetSignalColor(concentrationDirection));
    }

    // 분석이 끝나는 순간 결과 창을 한 번만 띄운다.
    private void ShowCompletionOnce()
    {
        if (_completionShown || _state == null || !_state.IsCompleted)
        {
            return;
        }

        _completionShown = true;

        MissionUIController controller = GetComponent<MissionUIController>();
        if (controller != null)
        {
            controller.MarkCompletionReady();
            controller.ShowCompletedState();
        }
    }

    // 패널을 켜고 끈다. 켤 때만 페이드를 넣어 화면이 갑자기 튀지 않게 한다.
    private void SetPanelVisible(GameObject panel, bool visible, bool animate)
    {
        if (panel == null)
        {
            return;
        }

        if (!panel.TryGetComponent(out CanvasGroup canvasGroup))
        {
            panel.SetActive(visible);
            return;
        }

        canvasGroup.DOKill();

        if (!visible)
        {
            panel.SetActive(false);
            return;
        }

        panel.SetActive(true);
        canvasGroup.alpha = animate ? 0f : 1f;

        if (animate)
        {
            // 미션 UI는 게임을 멈춘 상태에서 뜨므로 SetUpdate(true)로 타임스케일을 무시한다.
            canvasGroup.DOFade(1f, PanelFadeSeconds).SetUpdate(true);
        }
    }

    private void KillWaveformTweens()   // 화면이 꺼지면 DOTween이 계속 돌아서 꺼준다
    {
        _observerView.KillWaveformTween();
        _temperatureView.KillWaveformTween();
        _concentrationView.KillWaveformTween();
    }

    //--- 첫 역할 정하는 창에서 버튼(색)과 안내 문구(TMP_Text)를 가져와 갱신한다 ---//
    private Button GetInitialButton(SampleAnalysisRole role)
    {
        switch (role)
        {
            case SampleAnalysisRole.Observer: return _initialObserverButton;
            case SampleAnalysisRole.Temperature: return _initialTemperatureButton;
            default: return _initialConcentrationButton;
        }
    }

    private TMP_Text GetInitialLabel(SampleAnalysisRole role)
    {
        switch (role)
        {
            case SampleAnalysisRole.Observer: return _initialObserverSlotText;
            case SampleAnalysisRole.Temperature: return _initialTemperatureSlotText;
            default: return _initialConcentrationSlotText;
        }
    }

    private Button GetRoleChangeButton(SampleAnalysisRole role)
    {
        switch (role)
        {
            case SampleAnalysisRole.Observer: return _observerButton;
            case SampleAnalysisRole.Temperature: return _temperatureButton;
            default: return _concentrationButton;
        }
    }

    private TMP_Text GetRoleChangeLabel(SampleAnalysisRole role)
    {
        switch (role)
        {
            case SampleAnalysisRole.Observer: return _observerLabel;
            case SampleAnalysisRole.Temperature: return _temperatureLabel;
            default: return _concentrationLabel;
        }
    }

    // 역할 패널 하나가 쓰는 본문 참조 묶음
    private RolePanelView GetView(SampleAnalysisRole role)
    {
        switch (role)
        {
            case SampleAnalysisRole.Observer: return _observerView;
            case SampleAnalysisRole.Temperature: return _temperatureView;
            default: return _concentrationView;
        }
    }

    // role enum을 화면에 보여줄 한글 이름으로 바꾸는 함수입니다.
    private static string GetRoleName(SampleAnalysisRole role)
    {
        switch (role)
        {
            case SampleAnalysisRole.Observer: return ObserverName;
            case SampleAnalysisRole.Temperature: return TemperatureName;
            default: return ConcentrationName;
        }
    }

    // 방향값은 "어느 쪽으로 조절해야 하는지"라서 1이면 지금이 낮은 상태다.
    private static string GetTemperatureStatusText(int direction)
    {
        if (direction > 0)
        {
            return "온도 낮음";
        }

        return direction < 0 ? "온도 높음" : "온도 적정 범위";
    }

    // 농도는 낮음/높음보다 부족/과포화가 플레이어에게 더 직관적이다.
    private static string GetConcentrationStatusText(int direction)
    {
        if (direction > 0)
        {
            return "농도 부족";
        }

        return direction < 0 ? "농도 과포화" : "농도 적정";
    }

    private static string GetActivationText(int direction)
    {
        return direction == 0 ? "생체반응 활성화" : "생체반응 비활성화";
    }

    private static string GetDirectionText(int direction)
    {
        if (direction > 0)
        {
            return "올리기";
        }

        return direction < 0 ? "내리기" : "적정 범위";
    }

    // 적정은 초록, 부족·낮음은 노랑, 과포화·높음은 주황으로 가른다.
    private static Color GetSignalColor(int direction)
    {
        if (direction == 0)
        {
            return new Color(0.2f, 1f, 0.58f, 1f);
        }

        return direction > 0
            ? new Color(0.95f, 0.9f, 0.22f, 1f)
            : new Color(1f, 0.48f, 0.18f, 1f);
    }

    // 나빠질수록 색이 초록 → 노랑 → 주황 → 빨강으로 간다. 수치를 못 보는 P1이 색만으로 단계를 읽는다.
    private static Color GetWaveformColor(SampleReactionLevel level)
    {
        switch (level)
        {
            case SampleReactionLevel.Stable: return new Color(0.2f, 1f, 0.58f, 1f);
            case SampleReactionLevel.Detected: return new Color(0.95f, 0.9f, 0.22f, 1f);
            case SampleReactionLevel.Unstable: return new Color(1f, 0.55f, 0.16f, 1f);
            default: return new Color(1f, 0.12f, 0.12f, 1f);
        }
    }

    // 나빠질수록 크게 흔들린다. 다만 파형 영역을 넘어가면 위아래 문구를 가리므로 폭을 좁게 잡는다.
    private static float GetWaveformShake(SampleReactionLevel level)
    {
        switch (level)
        {
            case SampleReactionLevel.Stable: return 1f;
            case SampleReactionLevel.Detected: return 4f;
            case SampleReactionLevel.Unstable: return 9f;
            default: return 16f;
        }
    }

    private static float GetWaveformRotation(SampleReactionLevel level)
    {
        switch (level)
        {
            case SampleReactionLevel.Stable: return 1.5f;
            case SampleReactionLevel.Detected: return 5f;
            case SampleReactionLevel.Unstable: return 11f;
            default: return 20f;
        }
    }

    // 나빠질수록 주기가 짧아진다. 같은 흔들림이라도 빠르면 더 급해 보인다.
    private static float GetWaveformDuration(SampleReactionLevel level)
    {
        switch (level)
        {
            case SampleReactionLevel.Stable: return 0.62f;
            case SampleReactionLevel.Detected: return 0.44f;
            case SampleReactionLevel.Unstable: return 0.24f;
            default: return 0.12f;
        }
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
        {
            text.text = value;
        }
    }

    private static void SetColor(Image image, Color color)
    {
        if (image != null)
        {
            image.color = color;
        }
    }

    // 역할 패널 하나가 쓰는 본문 참조 묶음이다. 공통 상단 버튼과 관찰 전용 좌우 힌트는 여기 넣지 않는다.
    [Serializable]
    private sealed class RolePanelView
    {
        [Header("분석 정보")]
        [SerializeField] private Slider _stableProgress;
        [SerializeField] private TMP_Text _progressText;
        [SerializeField] private TMP_Text _valueText;

        [Header("파형 영역")]
        // 전체 반응을 보여주는 파형이다. 온도·농도를 따로 보여주는 화면은 아래 둘도 함께 쓴다.
        [SerializeField] private RectTransform _waveformRoot;
        [SerializeField] private RectTransform _temperatureWaveformRoot;
        [SerializeField] private RectTransform _concentrationWaveformRoot;

        private bool _waveformTweenActive;
        // 아직 아무 단계도 그리지 않았다는 표시다. 첫 갱신에서 반드시 한 번 실행되게 한다.
        private SampleReactionLevel _currentWaveformLevel = (SampleReactionLevel)(-1);

        public Slider StableProgress => _stableProgress;
        public TMP_Text ProgressText => _progressText;
        public TMP_Text ValueText => _valueText;

        // 단계가 그대로면 돌고 있는 애니메이션을 건드리지 않는다.
        // Redraw는 값이 밀릴 때마다 불리는데, 그때마다 다시 걸면 처음으로 돌아가 멈춘 것처럼 보인다.
        public void PlayWaveform(SampleReactionLevel level, int temperatureDirection, int concentrationDirection)
        {
            if (_currentWaveformLevel == level && _waveformTweenActive)
            {
                return;
            }

            if (_waveformRoot == null && _temperatureWaveformRoot == null && _concentrationWaveformRoot == null)
            {
                return;
            }

            KillWaveformTween();
            _currentWaveformLevel = level;
            _waveformTweenActive = true;

            float shake = GetWaveformShake(level);
            float rotation = GetWaveformRotation(level);
            float duration = GetWaveformDuration(level);

            PlayWaveformRoot(_waveformRoot, level, GetWaveformColor(level), shake, rotation, duration);

            // 온도·농도 전용 영역은 각자의 방향값만 보고 색과 단계를 정한다.
            // 같은 RectTransform이 두 번 들어오면 Tween이 서로를 덮어쓰므로 중복은 건너뛴다.
            if (_temperatureWaveformRoot != _waveformRoot)
            {
                PlayWaveformRoot(
                    _temperatureWaveformRoot, GetSignalLevel(level, temperatureDirection),
                    GetSignalColor(temperatureDirection), shake, rotation, duration);
            }

            if (_concentrationWaveformRoot != _waveformRoot
                && _concentrationWaveformRoot != _temperatureWaveformRoot)
            {
                PlayWaveformRoot(
                    _concentrationWaveformRoot, GetSignalLevel(level, concentrationDirection),
                    GetSignalColor(concentrationDirection), shake, rotation, duration);
            }
        }

        // 모든 Tween에 각자의 파형 루트를 타깃으로 달아 두었으므로 루트 단위로 한 번에 정리된다.
        public void KillWaveformTween()
        {
            KillTweensOn(_waveformRoot);
            KillTweensOn(_temperatureWaveformRoot);
            KillTweensOn(_concentrationWaveformRoot);
            _waveformTweenActive = false;
        }

        private static void KillTweensOn(RectTransform root)
        {
            if (root != null)
            {
                DOTween.Kill(root);
            }
        }

        // 개별 신호는 적정이면 안정, 벗어나면 반응 감지로 표시한다.
        // 다만 전체가 붕괴 단계면 개별 파형도 붕괴로 맞춰 화면 전체가 같은 위험 신호를 보내게 한다.
        private static SampleReactionLevel GetSignalLevel(SampleReactionLevel totalLevel, int direction)
        {
            if (direction == 0)
            {
                return SampleReactionLevel.Stable;
            }

            return totalLevel == SampleReactionLevel.Collapse
                ? SampleReactionLevel.Collapse
                : SampleReactionLevel.Detected;
        }

        // 파형 영역 하나에 들어 있는 Image 자식들을 찾아 막대형/선분형에 맞게 움직인다.
        private void PlayWaveformRoot(
            RectTransform root, SampleReactionLevel level, Color color, float shake, float rotation, float duration)
        {
            if (root == null)
            {
                return;
            }

            // 영역 자신의 Image는 배경이라 움직이지 않는다. 그래서 자식만 골라 세고, 몇 번째인지도 따로 센다.
            int animatedCount = 0;
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image.rectTransform == root)
                {
                    continue;
                }

                if (image.name.StartsWith(WaveBarNamePrefix, StringComparison.Ordinal))
                {
                    AnimateWaveBar(root, image, animatedCount, level, color, duration);
                }
                else
                {
                    AnimateWaveSegment(root, image, animatedCount, level, color, shake, rotation, duration);
                }

                animatedCount++;
            }

            if (animatedCount == 0)
            {
                Debug.LogWarning($"[샘플 분석] '{root.name}' 안에 움직일 파형 자식이 없습니다.", root);
            }
        }

        // 막대형은 높이만 출렁이게 해서 실시간 신호 패널처럼 보이게 한다.
        private void AnimateWaveBar(
            RectTransform root, Image bar, int index, SampleReactionLevel level, Color color, float duration)
        {
            if (bar.transform is not RectTransform rect)
            {
                return;
            }

            rect.DOKill();
            bar.DOKill();

            Vector2 startSize = rect.sizeDelta;

            // 가로 폭을 앵커로만 늘려 두면 sizeDelta.x가 0이라서, 높이 Tween을 걸는 순간 막대가 사라진다.
            if (startSize.x <= 0.01f)
            {
                startSize.x = 8f;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, startSize.x);
            }

            // 막대마다 조금씩 다른 높이로 흔들리게 해서 여러 개가 한 덩어리로 움직이지 않게 한다.
            float phase = 0.35f + Mathf.Abs(Mathf.Sin(index * 0.73f)) * 0.65f;
            float height = Mathf.Max(8f, startSize.y + GetWaveformShake(level) * phase);

            bar.color = color;
            rect.DOSizeDelta(new Vector2(startSize.x, height), duration)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true)
                .SetTarget(root);

            if (level == SampleReactionLevel.Collapse)
            {
                bar.DOFade(0.25f, WaveformFadeSeconds)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.Flash)
                    .SetUpdate(true)
                    .SetTarget(root);
            }
        }

        // 선분형은 위아래로 흔들리며 돌아간다. 홀짝으로 방향을 뒤집어 서로 반대로 움직이게 한다.
        private void AnimateWaveSegment(
            RectTransform root, Image segment, int index, SampleReactionLevel level,
            Color color, float shake, float rotation, float duration)
        {
            if (segment.transform is not RectTransform rect)
            {
                return;
            }

            rect.DOKill();
            segment.DOKill();

            Vector2 startPosition = rect.anchoredPosition;
            Vector3 startRotation = rect.localEulerAngles;
            Vector3 startScale = rect.localScale;
            float direction = index % 2 == 0 ? 1f : -1f;

            segment.color = color;

            // 흔들림과 회전은 서로 다른 Tween으로 걸어야 한다. 하나로 묶으면 회전이 먼저 끝나고 흔들림만 남아, 각도가 고정돼 보인다.
            rect.DOAnchorPosY(startPosition.y + shake * direction, duration)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true)
                .SetTarget(root);

            rect.DOLocalRotate(new Vector3(0f, 0f, startRotation.z + rotation * direction), duration)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true)
                .SetTarget(root);

            rect.DOScale(startScale * (level == SampleReactionLevel.Collapse ? 1.18f : 1.05f), duration)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true)
                .SetTarget(root);

            // 마지막 단계에서만 깜빡임을 더해, 색만으로 구분이 어려울 때도 위험을 알 수 있게 한다.
            if (level == SampleReactionLevel.Collapse)
            {
                segment.DOFade(0.25f, WaveformFadeSeconds)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.Flash)
                    .SetUpdate(true)
                    .SetTarget(root);
            }
        }
    }
}
