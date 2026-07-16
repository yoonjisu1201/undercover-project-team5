using System;
using Cysharp.Threading.Tasks;
using TMPro;
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

    [Header("Menu")]
    [SerializeField] private GameObject _settingsPanel;

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

    private CustomInputActions _actions;

    private void Awake()
    {
        _actions = new CustomInputActions();
        _settingsPanel.SetActive(false);

        _bgmSlider.onValueChanged.AddListener(SetBgmVolume);
        _sfxSlider.onValueChanged.AddListener(SetSfxVolume);
        _voiceSlider.onValueChanged.AddListener(SetVoiceVolume);
        _micSlider.onValueChanged.AddListener(SetMicVolume);

        InitailizeVolumeSliders();
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
    private void SetBgmVolume(float value)
    {
        float decibel = value <= 0.0001f ? -80f : Mathf.Log10(value) * 20f;

        _audioMixer.SetFloat("BgmVolume", decibel);
        PlayerPrefs.SetFloat(BgmVolumeKey, value);
    }

    private void SetSfxVolume(float value)
    {
        float decibel = value <= 0.0001f ? -80f : Mathf.Log10(value) * 20f;

        _audioMixer.SetFloat("SfxVolume", decibel);
        PlayerPrefs.SetFloat(SfxVolumeKey, value);
    }

    private void SetVoiceVolume(float value)
    {
        int vivoxVolume = NormalizedToVivoxVolume(value);

        if (VivoxManager.IsLoggedIn)
        {
            VivoxService.Instance.SetOutputDeviceVolume(vivoxVolume);
        }

        PlayerPrefs.SetFloat(VoiceVolumeKey, value);
    }

    private void SetMicVolume(float value)
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

    private async UniTaskVoid ApplyVivoxVolumesAsync()
    {
        await VivoxManager.LoginTask;

        SetVoiceVolume(_voiceSlider.value);
        SetMicVolume(_micSlider.value);
    }

    //--- MonoBehaviour 이벤트 메서드 ---//
    private void OnEnable()
    {
        _actions.System.Enable();
        _actions.System.Escape.performed += OnEscape;

        VivoxManager.Instance.AudioDevicesChanged += RefreshDeviceNames;
        VivoxManager.Instance.MicTestStateChanged += RefreshMicTestButtonText;
        RefreshDeviceNames();
        RefreshMicTestButtonText(VivoxManager.Instance.IsMicTesting);
        ApplyVivoxVolumesAsync().Forget();
    }

    private void OnDisable()
    {
        _actions.System.Escape.performed -= OnEscape;
        _actions.Disable();

        VivoxManager.Instance.AudioDevicesChanged -= RefreshDeviceNames;
        VivoxManager.Instance.MicTestStateChanged -= RefreshMicTestButtonText;
        VivoxManager.Instance.StopMicTest();
    }

    private void OnDestroy()
    {
        _bgmSlider.onValueChanged.RemoveListener(SetBgmVolume);
        _sfxSlider.onValueChanged.RemoveListener(SetSfxVolume);
        _voiceSlider.onValueChanged.RemoveListener(SetVoiceVolume);
        _micSlider.onValueChanged.RemoveListener(SetMicVolume);
    }

    //--- OnClick 이벤트 핸들러 ---//
    private void OnEscape(InputAction.CallbackContext context)
    {
        SetMenuActive(!_settingsPanel.activeSelf);
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

    public void MicTestButtonPressed()
    {
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
        _inputDeviceText.text = VivoxManager.Instance.CurrentInputDeviceName;

        _outputDeviceText.text = VivoxManager.Instance.CurrentOutputDeviceName;
    }

    public void ReturnToGame()
    {
        SetMenuActive(false);
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
