using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

public sealed class GameSettingsMenu : MonoBehaviour
{
    private const string BgmVolumeKey = "BgmVolume";
    private const string SfxVolumeKey = "SfxVolume";
    private const string VoiceVolumeKey = "VoiceVolume";
    private const string MicVolumeKey = "MicVolume";
    private const string ResolutionIndexKey = "ResolutionIndex";
    private const string FullScreenKey = "FullScreen";
    private const string MouseSensitivityKey = "MouseSensitivity";
    private const string UiJumpShakeKey = "UiJumpShake";
    // Unity Localization 패키지의 PlayerPrefLocaleSelector 가 시작할 때 읽는 PlayerPrefs 키.
    // 이름이 다르면 저장은 되어도 다음 실행에서 복원되지 않는다.
    private const string LocaleKey = "selected-locale";

    // 지원되는 해상도 목록 (가로 x 세로)
    private static readonly Vector2Int[] SupportedResolutions =
    {
        new(640, 360),
        new(854, 480),
        new(1280, 720),
        new(1920, 1080),
        new(2560, 1440),
        new(3840, 2160)
    };

    [Header("Menu")]
    [SerializeField] private GameObject _settingsPanel;
    [SerializeField] private GameObject[] _tabPanels;
    [SerializeField] private Button[] _tabButtons;
    [SerializeField] private Color _activeTabColor = new(0.08f, 0.55f, 0.62f, 1f);
    [SerializeField] private Color _inactiveTabColor = new(0.05f, 0.31f, 0.36f, 1f);

    [Header("Device Settings")]
    [SerializeField] private TMP_Text _inputDeviceText;
    [SerializeField] private TMP_Text _outputDeviceText;
    [SerializeField] private TMP_Text _micTestButtonText;
    [SerializeField] private TMP_Text _micMuteButtonText;
    [SerializeField] private TMP_Text _speakerMuteButtonText;

    // 조건에 따라 문구가 바뀌므로 LocalizeStringEvent 로는 안 되고 코드에서 조회해야 한다.
    [Header("Localized Strings")]
    [SerializeField] private LocalizedString _micTestStartText;
    [SerializeField] private LocalizedString _micTestStopText;
    [SerializeField] private LocalizedString _noInputDeviceText;
    [SerializeField] private LocalizedString _noOutputDeviceText;
    [SerializeField] private LocalizedString _onText;
    [SerializeField] private LocalizedString _offText;

    [Header("Volume")]
    [SerializeField] private AudioMixer _audioMixer;
    [SerializeField] private Slider _bgmSlider;
    [SerializeField] private Slider _sfxSlider;
    [SerializeField] private Slider _voiceSlider;
    [SerializeField] private Slider _micSlider;

    [Header("Graphics Settings")]
    [SerializeField] private TMP_Text _resolutionText;
    [SerializeField] private Toggle _fullScreenToggle;
    [SerializeField] private Toggle _uiJumpShakeToggle;

    [Header("Gameplay Settings")]
    [SerializeField] private Slider _sensitivitySlider;
    // 슬라이더로는 미세 조절이 어려우므로 숫자를 직접 입력할 수도 있게 한다.
    [SerializeField] private TMP_InputField _sensitivityInput;
    // ◀ ▶ 사이에 현재 언어 이름을 보여주는 텍스트.
    [SerializeField] private TMP_Text _languageText;

    private CustomInputActions _actions;
    private bool _vivoxEventsSubscribed;
    private int _resolutionIndex;

    private void Awake()
    {
        CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;

        _actions = new CustomInputActions();
        _settingsPanel.SetActive(false);

        InitailizeVolumeSliders();
        InitializeGraphicsSettings();
        InitializeUiJumpShake();
        InitializeSensitivity();
        RefreshLanguageText();
        SelectTab(0);
    }

    //--- 초기화 메서드 ---//
    private void InitializeGraphicsSettings()
    {
        if (_resolutionText == null || _fullScreenToggle == null)
        {
            return;
        }

        int defaultIndex = FindClosestResolutionIndex(Screen.width, Screen.height);
        _resolutionIndex = Mathf.Clamp(PlayerPrefs.GetInt(ResolutionIndexKey, defaultIndex), 0, SupportedResolutions.Length - 1);

        bool isFullScreen = PlayerPrefs.GetInt(FullScreenKey, Screen.fullScreen ? 1 : 0) == 1;
        _fullScreenToggle.SetIsOnWithoutNotify(isFullScreen);
        ApplyResolution(isFullScreen);
    }

    // 해상도 참조가 비어 있으면 InitializeGraphicsSettings 가 통째로 빠져나가므로 따로 둔다.
    // 이 설정은 해상도와 아무 관계가 없다.
    private void InitializeUiJumpShake()
    {
        bool isEnabled = PlayerPrefs.GetInt(UiJumpShakeKey, 1) == 1;

        _uiJumpShakeToggle?.SetIsOnWithoutNotify(isEnabled);
        UiJumpShake.IsEnabled = isEnabled;
    }

    // 토글에 연결한다. 점프·착지할 때 UI 가 흔들리는 연출을 끈다.
    public void SetUiJumpShake(bool isEnabled)
    {
        PlayerPrefs.SetInt(UiJumpShakeKey, isEnabled ? 1 : 0);
        UiJumpShake.IsEnabled = isEnabled;
    }

    private void InitializeSensitivity()
    {
        float sensitivity = Mathf.Clamp(
            PlayerPrefs.GetFloat(MouseSensitivityKey, PlayerCameraController.DefaultSensitivity),
            PlayerCameraController.MinSensitivity,
            PlayerCameraController.MaxSensitivity);
        _sensitivitySlider?.SetValueWithoutNotify(sensitivity);
        ApplyMouseSensitivity(sensitivity);
    }

    public void SelectTab(int tabIndex)
    {
        if (_tabPanels == null)
        {
            return;
        }

        for (int i = 0; i < _tabPanels.Length; i++)
        {
            bool selected = i == tabIndex;
            _tabPanels[i].SetActive(selected);

            if (_tabButtons != null && i < _tabButtons.Length)
            {
                Button tabButton = _tabButtons[i];
                // ColorTint는 Image 색에 곱해지므로 탭 Image는 흰색이어야 이 값이 그대로 나온다.
                // 네 상태를 모두 지정하지 않으면 남은 상태에 기본 회색이 남아 클릭할 때 번쩍인다.
                ColorBlock colors = tabButton.colors;
                colors.normalColor = _inactiveTabColor;
                colors.highlightedColor = _activeTabColor;
                colors.pressedColor = _activeTabColor;
                colors.selectedColor = _activeTabColor;
                colors.disabledColor = _activeTabColor;
                tabButton.colors = colors;
                tabButton.interactable = !selected;
            }
        }
    }

    //--- 해상도 적용 메서드 ---//
    private int FindClosestResolutionIndex(int width, int height)   // 주어진 해상도와 가장 가까운 지원 해상도의 인덱스를 반환
    {
        if (SupportedResolutions == null || SupportedResolutions.Length == 0)
        {
            return 0;
        }

        int closestIndex = 0;
        int closestDifference = int.MaxValue;

        for (int i = 0; i < SupportedResolutions.Length; i++)
        {
            Vector2Int resolution = SupportedResolutions[i];
            int difference = Mathf.Abs(resolution.x - width) + Mathf.Abs(resolution.y - height);

            if (difference < closestDifference)
            {
                closestDifference = difference;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    // 해상도 승인 메서드
    private void ApplyResolution(bool isFullScreen)
    {
        Vector2Int resolution = SupportedResolutions[_resolutionIndex];
        Screen.SetResolution(resolution.x, resolution.y, isFullScreen);
        _resolutionText.text = $"{resolution.x} × {resolution.y}";
        PlayerPrefs.SetInt(ResolutionIndexKey, _resolutionIndex);
    }

    public void SetFullScreen(bool isFullScreen)    // 전체 화면 모드 설정
    {
        Screen.fullScreen = isFullScreen;
        PlayerPrefs.SetInt(FullScreenKey, isFullScreen ? 1 : 0);
    }

    // 입력창에 숫자를 넣고 엔터를 치거나 포커스를 옮겼을 때. 범위를 벗어나면 잘라내고,
    // 숫자가 아니면 현재 값으로 되돌린다.
    private void HandleSensitivityInput(string text)
    {
        if (!float.TryParse(text, out float sensitivity))
        {
            ApplyMouseSensitivity(PlayerPrefs.GetFloat(MouseSensitivityKey, PlayerCameraController.DefaultSensitivity));
            return;
        }

        sensitivity = Mathf.Clamp(sensitivity,
            PlayerCameraController.MinSensitivity,
            PlayerCameraController.MaxSensitivity);

        _sensitivitySlider?.SetValueWithoutNotify(sensitivity);
        SetMouseSensitivity(sensitivity);
    }

    // 슬라이더를 움직여서 세팅
    public void SetMouseSensitivity(float sensitivity)
    {
        PlayerPrefs.SetFloat(MouseSensitivityKey, sensitivity);
        ApplyMouseSensitivity(sensitivity);
    }

    private void ApplyMouseSensitivity(float sensitivity)   // 마우스 감도 적용
    {
        if (_sensitivityInput != null)
        {
            // 입력창이 스스로 부른 갱신에서 다시 이벤트가 돌지 않도록 알림 없이 넣는다.
            _sensitivityInput.SetTextWithoutNotify(sensitivity.ToString("0.0"));
        }

        var playerObject = NetworkManager.Singleton?.LocalClient?.PlayerObject;

        if (playerObject != null &&
            playerObject.TryGetComponent(out PlayerCameraController playerCameraController))
        {
            playerCameraController.SetMouseSensitivity(sensitivity);
        }
    }

    //--- 언어 설정 메서드 ---//
    public void SelectPreviousLanguage()
    {
        ShiftLanguage(-1);
    }

    public void SelectNextLanguage()
    {
        ShiftLanguage(1);
    }

    // 사용 가능한 언어 목록에서 offset 만큼 이동한다. 목록 끝에서는 반대쪽 끝으로 돌아간다.
    private void ShiftLanguage(int offset)
    {
        var locales = LocalizationSettings.AvailableLocales.Locales;

        // locales.Count 를 더해 두면 offset 이 -1 일 때도 음수 인덱스가 나오지 않는다.
        int index = locales.IndexOf(LocalizationSettings.SelectedLocale);
        Locale locale = locales[(index + offset + locales.Count) % locales.Count];

        // 이 한 줄로 LocalizeStringEvent 와 SelectedLocaleChanged 구독자들이 전부 갱신된다.
        LocalizationSettings.SelectedLocale = locale;

        // PlayerPrefLocaleSelector 는 시작 시점에 한 번만 저장하고 언어 변경 시에는 저장하지 않는다.
        // 다음 실행 때 복원되려면 여기서 직접 기록해야 한다.
        PlayerPrefs.SetString(LocaleKey, locale.Identifier.Code);
        RefreshLanguageText();
    }

    // 어떤 언어로 보고 있든 읽을 수 있도록 각 언어의 자기 이름(한국어 / English)으로 표시한다.
    private void RefreshLanguageText()
    {
        if (_languageText == null)
        {
            return;
        }

        _languageText.text = LocalizationSettings.SelectedLocale.Identifier.CultureInfo.NativeName;
    }

    private void InitailizeVolumeSliders()
    {
        // PlayerPrefs에서 저장된 볼륨 값을 가져와서 슬라이더에 적용
        float bgmVolume = PlayerPrefs.GetFloat(BgmVolumeKey, 1f);
        float sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);
        float voiceVolume = PlayerPrefs.GetFloat(VoiceVolumeKey, 1f);
        float micVolume = PlayerPrefs.GetFloat(MicVolumeKey, 1f);

        _bgmSlider.SetValueWithoutNotify(bgmVolume);
        _sfxSlider.SetValueWithoutNotify(sfxVolume);
        _voiceSlider.SetValueWithoutNotify(voiceVolume);
        _micSlider.SetValueWithoutNotify(micVolume);

        SetBgmVolume(bgmVolume);
        SetSfxVolume(sfxVolume);
    }

    //--- 볼륨 설정 메서드 ---//
    public void SetBgmVolume(float value)
    {
        float decibel = value <= 0.0001f ? -80f : Mathf.Log10(value) * 20f;

        _audioMixer.SetFloat("BgmVolume", decibel);
        PlayerPrefs.SetFloat(BgmVolumeKey, value);
    }

    public void SetSfxVolume(float value)
    {
        float decibel = value <= 0.0001f ? -80f : Mathf.Log10(value) * 20f;

        _audioMixer.SetFloat("SfxVolume", decibel);
        PlayerPrefs.SetFloat(SfxVolumeKey, value);
    }

    public void SetVoiceVolume(float value)
    {
        int vivoxVolume = NormalizedToVivoxVolume(value);

        if (VivoxManager.IsLoggedIn)
        {
            VivoxService.Instance.SetOutputDeviceVolume(vivoxVolume);
        }

        PlayerPrefs.SetFloat(VoiceVolumeKey, value);
    }

    public void SetMicVolume(float value)
    {
        int vivoxVolume = NormalizedToVivoxVolume(value);

        if (VivoxManager.IsLoggedIn)
        {
            VivoxService.Instance.SetInputDeviceVolume(vivoxVolume);
        }

        PlayerPrefs.SetFloat(MicVolumeKey, value);
    }

    private int NormalizedToVivoxVolume(float value)
    {
        value = Mathf.Clamp01(value);
        return Mathf.RoundToInt(Mathf.Lerp(-50f, 0f, value));
    }

    //--- MonoBehaviour 이벤트 메서드 ---//
    private void OnEnable()
    {
        _actions.System.Enable();
        _actions.System.Escape.performed += OnEscape;

        // 프리팹 UnityEvent 는 메서드 이름으로 묶여 조용히 끊기므로 코드에서 구독한다.
        // onEndEdit 은 엔터와 포커스 이탈 모두에서 불린다.
        if (_sensitivityInput != null)
        {
            _sensitivityInput.onEndEdit.AddListener(HandleSensitivityInput);
        }

        LocalizationSettings.SelectedLocaleChanged += HandleLocaleChanged;

        InitializeVivoxSettingsAsync().Forget();
    }

    private void OnDisable()
    {
        _actions.System.Escape.performed -= OnEscape;
        _actions.Disable();

        LocalizationSettings.SelectedLocaleChanged -= HandleLocaleChanged;

        if (_sensitivityInput != null)
        {
            _sensitivityInput.onEndEdit.RemoveListener(HandleSensitivityInput);
        }

        if (_vivoxEventsSubscribed && VivoxManager.Instance != null)
        {
            VivoxManager.Instance.AudioDevicesChanged -= RefreshDeviceNames;
            VivoxManager.Instance.MicTestStateChanged -= RefreshMicTestButtonText;
            VivoxManager.Instance.MuteStateChanged -= RefreshAudioToggleButtonTexts;
            _vivoxEventsSubscribed = false;
        }

        // 창을 닫으면 무조건 테스트를 끝내고 게임 채널로 돌아간다.
        // 마이크 테스트 버튼은 이벤트 구독과 무관하게 눌릴 수 있어서, 구독 여부를 따지면 안 된다.
        VivoxManager.Instance?.StopMicTest();
    }

    private async UniTaskVoid InitializeVivoxSettingsAsync()
    {
        // Vivox 로그인을 기다리는 동안 씬이 바뀌어 이 오브젝트가 파괴될 수 있다.
        // 파괴 토큰을 물려두지 않으면 대기가 계속 돌다가 완료 시점에 이어서 실행되고,
        // 파괴된 컴포넌트의 isActiveAndEnabled에 접근하는 순간 MissingReferenceException이 난다.
        // (Unity 오브젝트는 == null 비교로만 파괴를 확인할 수 있고, 멤버 접근은 예외를 던진다.)
        CancellationToken cancellationToken = this.GetCancellationTokenOnDestroy();

        await UniTask.WaitUntil(() => VivoxManager.Instance != null, cancellationToken: cancellationToken);
        await UniTask.WaitUntil(() => VivoxManager.IsLoggedIn, cancellationToken: cancellationToken);

        if (!isActiveAndEnabled || _vivoxEventsSubscribed)
        {
            return;
        }

        VivoxManager.Instance.AudioDevicesChanged += RefreshDeviceNames;
        VivoxManager.Instance.MicTestStateChanged += RefreshMicTestButtonText;
        VivoxManager.Instance.MuteStateChanged += RefreshAudioToggleButtonTexts;
        _vivoxEventsSubscribed = true;

        RefreshDeviceNames();
        RefreshMicTestButtonText(VivoxManager.Instance.IsMicTesting);
        RefreshAudioToggleButtonTexts();
        SetVoiceVolume(_voiceSlider.value);
        SetMicVolume(_micSlider.value);
    }

    //--- OnClick 이벤트 핸들러 ---//
    private void OnEscape(InputAction.CallbackContext context)
    {
        // 설정창 위에 단서·가이드북 등이 떠 있으면 가장 위 UI부터 닫는다.
        if (GameplayUiMode.Instance != null && GameplayUiMode.Instance.CloseTopUi())
        {
            return;
        }

        if (_settingsPanel.activeSelf)
        {
            ReturnToGame();
            return;
        }

        SetMenuActive(true);
    }

    public void OpenSettings()
    {
        SetMenuActive(true);
    }

    private void SetMenuActive(bool active)
    {
        _settingsPanel.SetActive(active);

        if (active)
        {
            RefreshAudioToggleButtonTexts();
            GameplayUiMode.Instance?.ActivateCursor();  // 커서 활성화
        }
        else
        {
            GameplayUiMode.Instance?.DeactivateCursor();    // 커서 비활성화

            // 창을 닫으면 마이크 테스트를 끝내고 게임 채널로 돌아간다.
            // 이 스크립트가 붙은 오브젝트는 계속 켜져 있고 자식 패널만 숨기므로 OnDisable이 불리지 않는다.
            // ESC·뒤로가기 등 닫는 경로가 모두 여기를 지나가니 여기서 처리해야 한다.
            VivoxManager.Instance?.StopMicTest();
        }
    }

    public void SelectPreviousInputDevice()
    {
        VivoxManager.Instance.SelectInputDeviceAsync(-1).Forget();
    }

    public void SelectNextInputDevice()
    {
        VivoxManager.Instance.SelectInputDeviceAsync(1).Forget();
    }

    public void SelectPreviousOutputDevice()
    {
        VivoxManager.Instance.SelectOutputDeviceAsync(-1).Forget();
    }

    public void SelectNextOutputDevice()
    {
        VivoxManager.Instance.SelectOutputDeviceAsync(1).Forget();
    }

    public void SelectPreviousResolution()
    {
        _resolutionIndex =
            (_resolutionIndex - 1 + SupportedResolutions.Length) % SupportedResolutions.Length;
        ApplyResolution(_fullScreenToggle != null && _fullScreenToggle.isOn);
    }

    public void SelectNextResolution()
    {
        _resolutionIndex = (_resolutionIndex + 1) % SupportedResolutions.Length;
        ApplyResolution(_fullScreenToggle != null && _fullScreenToggle.isOn);
    }

    public void MicTestButtonPressed()
    {
        RefreshMicTestButtonText(!VivoxManager.Instance.IsMicTesting);
        VivoxManager.Instance.ToggleMicTest();
    }

    // 마이크 테스트 버튼과 장치 이름은 상태에 따라 문구가 바뀌어 LocalizeStringEvent 로 처리할 수 없고,
    // 코드가 한 번 채워 넣은 뒤로는 스스로 갱신되지 않는다. 언어가 바뀌면 여기서 다시 채운다.
    private void HandleLocaleChanged(Locale locale)
    {
        RefreshAudioToggleButtonTexts();

        // Vivox 로그인 전에는 표시할 장치 정보 자체가 없다.
        if (!_vivoxEventsSubscribed)
        {
            return;
        }

        RefreshMicTestButtonText(VivoxManager.Instance.IsMicTesting);
        RefreshDeviceNames();
    }

    private void RefreshMicTestButtonText(bool isTesting)
    {
        _micTestButtonText.text = (isTesting ? _micTestStopText : _micTestStartText).GetLocalizedString();
    }

    private void RefreshAudioToggleButtonTexts()
    {
        if (_micMuteButtonText != null)
        {
            _micMuteButtonText.text = (VivoxManager.IsMicMuted ? _offText : _onText).GetLocalizedString();
        }

        if (_speakerMuteButtonText != null)
        {
            _speakerMuteButtonText.text = (VivoxManager.IsOutputMuted ? _offText : _onText).GetLocalizedString();
        }
    }

    private void RefreshDeviceNames()
    {
        _inputDeviceText.text = GetDeviceDisplayName(
            VivoxManager.Instance.CurrentInputDeviceName,
            _noInputDeviceText.GetLocalizedString());
        _outputDeviceText.text = GetDeviceDisplayName(
            VivoxManager.Instance.CurrentOutputDeviceName,
            _noOutputDeviceText.GetLocalizedString());
    }

    private static string GetDeviceDisplayName(string deviceName, string emptyDeviceName)
    {
        return string.IsNullOrWhiteSpace(deviceName) || deviceName == emptyDeviceName
            ? "Default"
            : deviceName;
    }

    // 로컬 플레이어에게 긴급 탈출을 요청하고 설정 메뉴를 닫는다.
    public void EmergencyEscape()
    {
        // 현재 클라이언트가 소유한 플레이어 오브젝트를 가져온다.
        var playerObject = NetworkManager.Singleton.LocalClient?.PlayerObject;

        // 긴급 탈출 컴포넌트가 있을 때만 서버에 탈출을 요청한다.
        if (playerObject != null &&
            playerObject.TryGetComponent(out PlayerEmergencyEscape emergencyEscape))
        {
            if (emergencyEscape.RequestEmergencyEscape())
            {
                // 긴급 탈출 요청이 가능할 때만 게임 화면으로 돌아간다.
                ReturnToGame();
            }
        }
    }
    public void ReturnToGame()
    {
        PlayerPrefs.Save();
        SetMenuActive(false);
    }

    public void ReturnToLobby()
    {
        GameSessionManager.Instance?.LeaveSession();
    }

    public void GameEnd()
    {
        PlayerPrefs.Save();

        // 방에 들어와 있는 상태로 그냥 종료하면 남은 인원이 전송 계층 타임아웃을 다 기다리게 된다.
        // 세션부터 정리해 끊김 통보가 나가도록 한다.
        GameSessionManager.Instance?.LeaveSession();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
