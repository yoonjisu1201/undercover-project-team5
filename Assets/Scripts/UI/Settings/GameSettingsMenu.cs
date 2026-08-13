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

    [Header("Volume")]
    [SerializeField] private AudioMixer _audioMixer;
    [SerializeField] private Slider _bgmSlider;
    [SerializeField] private Slider _sfxSlider;
    [SerializeField] private Slider _voiceSlider;
    [SerializeField] private Slider _micSlider;

    [Header("Graphics Settings")]
    [SerializeField] private TMP_Text _resolutionText;
    [SerializeField] private Toggle _fullScreenToggle;

    [Header("Gameplay Settings")]
    [SerializeField] private Slider _sensitivitySlider;
    [SerializeField] private TMP_Text _sensitivityValueText;

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
        InitializeSensitivity();
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

    private void InitializeSensitivity()
    {
        float sensitivity = PlayerPrefs.GetFloat(MouseSensitivityKey, 0.5f);
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
                ColorBlock colors = tabButton.colors;
                colors.normalColor = _inactiveTabColor;
                colors.highlightedColor = _activeTabColor;
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

    public void SetMouseSensitivity(float sensitivity)  // 마우스 감도 설정
    {
        PlayerPrefs.SetFloat(MouseSensitivityKey, sensitivity);
        ApplyMouseSensitivity(sensitivity);
    }

    private void ApplyMouseSensitivity(float sensitivity)   // 마우스 감도 적용
    {
        if (_sensitivityValueText != null)
        {
            _sensitivityValueText.text = sensitivity.ToString("0.00");
        }

        var playerObject = NetworkManager.Singleton?.LocalClient?.PlayerObject;

        if (playerObject != null &&
            playerObject.TryGetComponent(out PlayerCameraController playerCameraController))
        {
            playerCameraController.SetMouseSensitivity(sensitivity);
        }
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

        InitializeVivoxSettingsAsync().Forget();
    }

    private void OnDisable()
    {
        _actions.System.Escape.performed -= OnEscape;
        _actions.Disable();

        if (_vivoxEventsSubscribed && VivoxManager.Instance != null)
        {
            VivoxManager.Instance.AudioDevicesChanged -= RefreshDeviceNames;
            VivoxManager.Instance.MicTestStateChanged -= RefreshMicTestButtonText;
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
        _vivoxEventsSubscribed = true;

        RefreshDeviceNames();
        RefreshMicTestButtonText(VivoxManager.Instance.IsMicTesting);
        SetVoiceVolume(_voiceSlider.value);
        SetMicVolume(_micSlider.value);
    }

    //--- OnClick 이벤트 핸들러 ---//
    private void OnEscape(InputAction.CallbackContext context)
    {
        if (_settingsPanel.activeSelf)
        {
            ReturnToGame();
            return;
        }

        // 떠 있는 UI(단서·가이드 북 등)가 있으면 맨 위 것부터 닫고, 다 닫혔을 때만 설정창을 연다.
        if (GameplayUiMode.Instance != null && GameplayUiMode.Instance.CloseTopUi())
        {
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

    private void RefreshMicTestButtonText(bool isTesting)
    {
        _micTestButtonText.text = isTesting
            ? "마이크 테스트 종료"
            : "마이크 테스트";
    }

    private void RefreshDeviceNames()
    {
        _inputDeviceText.text = GetDeviceDisplayName(
            VivoxManager.Instance.CurrentInputDeviceName,
            "입력 장치 없음");
        _outputDeviceText.text = GetDeviceDisplayName(
            VivoxManager.Instance.CurrentOutputDeviceName,
            "출력 장치 없음");
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

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
