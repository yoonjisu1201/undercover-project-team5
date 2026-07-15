using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.UI;

// Vivox 음성 서비스 초기화 + 로그인을 앱 시작 시 한 번만 수행한다.
// NetworkBootstrap의 UGS 로그인이 끝난 뒤에 실행되어야 한다.

public class VivoxManager : MonoBehaviour
{
	private const string DefaultSystemDeviceId = "Default System Device";
	private const string DefaultCommunicationDeviceId = "Default Communication Device";     // Vivox에서 제공하는 기본 시스템 장치 ID인데 사용 안할 것이므로 해당 장치 ID는 목록에서 제외하기 위해서

	public static VivoxManager Instance { get; private set; }

	public static bool IsLoggedIn => VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn;
	public static bool IsMicMuted => VivoxService.Instance != null && VivoxService.Instance.IsInputDeviceMuted;
	public static bool IsOutputMuted => VivoxService.Instance != null && VivoxService.Instance.IsOutputDeviceMuted;

	// 음성 채널 참가 전에 이 Task를 먼저 기다리면, 로그인 완료 시점과 무관하게 항상 안전하다.
	// Preserve()로 여러 곳에서 반복 await 가능하게 만든다.
	public static UniTask LoginTask { get; private set; }

	//--- 현재 참가 중인 테스트 채널과 세션 채널 이름을 저장하는 변수 ---//
	private string _sessionChannelName; // 실제 플레이 음성채널
	private string _micTestChannelName; // 마이크 테스트용 음성채널

	private bool _isMicTesting; // 마이크 테스트용 음성채널 참가 여부
	private bool _isChangingMicTest; // 마이크 테스트용 음성채널 참가 중 변경 여부

	private bool _inputWasMuted;
	private bool _outputWasMuted;

	public bool IsMicTesting => _isMicTesting;
	public string MicTestChannelName => _micTestChannelName;

	//--- Vivox Device 목록 가져오기 ---//
	public event Action AudioDevicesChanged;
	public event Action<bool> MicTestStateChanged;

	public string CurrentInputDeviceName => VivoxService.Instance?.ActiveInputDevice?.DeviceName ?? "입력 장치 없음";
	public string CurrentOutputDeviceName => VivoxService.Instance?.ActiveOutputDevice?.DeviceName ?? "출력 장치 없음";

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}
		Instance = this;
		DontDestroyOnLoad(gameObject);
	}

	private void Start()
	{
		LoginTask = LoginAsync().Preserve();
	}

	private async UniTask LoginAsync()
	{
		await NetworkBootstrap.SignInTask; // UGS 로그인 완료 후 Vivox 초기화

		const int maxAttempts = 3;
		for (int attempt = 1; attempt <= maxAttempts; attempt++)
		{
			try
			{
				await VivoxService.Instance.InitializeAsync();

				if (!VivoxService.Instance.IsLoggedIn)
				{
					await VivoxService.Instance.LoginAsync();
				}

				await SelectDefaultCommunicationDevicesAsync();
				SubscribeAudioDeviceEvents();

				Debug.Log($"[Vivox] 로그인 완료: {VivoxService.Instance.SignedInPlayerId}");
				return;
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[Vivox] 로그인 시도 {attempt}/{maxAttempts} 실패: {e.Message}");

				if (attempt == maxAttempts) throw;

				await UniTask.Delay(TimeSpan.FromSeconds(1));
			}
		}
	}
	public void JoinSessionChannel(string channelName)
	{
		JoinSessionChannelAsync(channelName).Forget();

	}
	private async UniTask JoinSessionChannelAsync(string channelName)
	{
		try
		{
			await LoginTask;
			await VivoxService.Instance.JoinGroupChannelAsync(channelName, ChatCapability.AudioOnly);
			_sessionChannelName = channelName;
			Debug.Log($"[Vivox] 채널 참가 완료: {channelName}");
		}
		catch (Exception e)
		{
			Debug.LogError($"[Vivox] 채널 참가 실패: {e.Message}");
		}
	}
	public void LeaveSessionChannel()
	{
		LeaveSessionChannelAsync().Forget();

	}
	private async UniTask LeaveSessionChannelAsync()
	{
		if (VivoxService.Instance == null || !VivoxService.Instance.IsLoggedIn) return;

		try
		{
			await VivoxService.Instance.LeaveAllChannelsAsync();
		}
		catch (Exception e)
		{
			Debug.LogError($"[Vivox] 채널 나가기 실패: {e.Message}");
		}
	}

	// 내 마이크(내가 말하는 소리)를 토글한다. 로그인 전이면 아무 동작도 하지 않는다.
	public void ToggleMicMute()
	{
		if (VivoxService.Instance == null || !VivoxService.Instance.IsLoggedIn)
		{
			Debug.LogWarning("[Vivox] 로그인 전에는 마이크 뮤트를 변경할 수 없습니다.");
			return;
		}

		if (VivoxService.Instance.IsInputDeviceMuted)
		{
			VivoxService.Instance.UnmuteInputDevice();
		}
		else
		{
			VivoxService.Instance.MuteInputDevice();
		}
	}

	// 내 스피커(다른 사람 목소리 듣기)를 토글한다. 로그인 전이면 아무 동작도 하지 않는다.
	public void ToggleOutputMute()
	{
		if (VivoxService.Instance == null || !VivoxService.Instance.IsLoggedIn)
		{
			Debug.LogWarning("[Vivox] 로그인 전에는 스피커 뮤트를 변경할 수 없습니다.");
			return;
		}

		if (VivoxService.Instance.IsOutputDeviceMuted)
		{
			VivoxService.Instance.UnmuteOutputDevice();
		}
		else
		{
			VivoxService.Instance.MuteOutputDevice();
		}
	}

	// Vivox 입력 장치 목록이 변경되었을 때 호출되는 콜백
	public async UniTask SelectInputDeviceAsync(int direction)
	{
		await LoginTask;

		var service = VivoxService.Instance;
		var devices = GetSelectableInputDevices();

		if (devices.Count == 0)
		{
			AudioDevicesChanged?.Invoke();
			return;
		}

		int currentIndex = FindInputDeviceIndex(devices, service.ActiveInputDevice?.DeviceID);

		int nextIndex = WrapIndex(currentIndex + direction, devices.Count);

		try
		{
			await service.SetActiveInputDeviceAsync(devices[nextIndex]);
			AudioDevicesChanged?.Invoke();
		}
		catch (Exception e)
		{
			Debug.LogError($"[Vivox] 입력 장치 변경 실패: {e.Message}");
		}
	}

	public async UniTask SelectOutputDeviceAsync(int direction)
	{
		await LoginTask;

		var service = VivoxService.Instance;
		var devices = GetSelectableOutputDevices();

		if (devices.Count == 0)
		{
			AudioDevicesChanged?.Invoke();
			return;
		}

		int currentIndex = FindOutputDeviceIndex(devices, service.ActiveOutputDevice?.DeviceID);

		int nextIndex = WrapIndex(currentIndex + direction, devices.Count);

		try
		{
			await service.SetActiveOutputDeviceAsync(devices[nextIndex]);
			AudioDevicesChanged?.Invoke();
		}
		catch (Exception e)
		{
			Debug.LogError($"[Vivox] 출력 장치 변경 실패: {e.Message}");
		}
	}

	private static int FindInputDeviceIndex(IReadOnlyList<VivoxInputDevice> devices, string deviceId)
	{
		for (int i = 0; i < devices.Count; i++)
		{
			if (devices[i].DeviceID == deviceId)
			{
				return i;
			}
		}
		return 0; // 현재 장치가 목록에 없으면 0 반환
	}

	private static int FindOutputDeviceIndex(IReadOnlyList<VivoxOutputDevice> devices, string deviceId)
	{
		for (int i = 0; i < devices.Count; i++)
		{
			if (devices[i].DeviceID == deviceId)
			{
				return i;
			}
		}
		return 0; // 현재 장치가 목록에 없으면 0 반환
	}

	private static int WrapIndex(int index, int count)
	{
		return (index % count + count) % count; // 음수 인덱스도 올바르게 처리
	}

	private static List<VivoxInputDevice> GetSelectableInputDevices()
	{
		var result = new List<VivoxInputDevice>();

		foreach (var device in VivoxService.Instance.AvailableInputDevices)
		{
			if (device.DeviceID == DefaultCommunicationDeviceId)
			{
				continue;
			}

			if (device.DeviceID == DefaultSystemDeviceId)
			{
				result.Insert(0, device);
			}
			else
			{
				result.Add(device);
			}
		}

		return result;
	}

	private static List<VivoxOutputDevice> GetSelectableOutputDevices()
	{
		var result = new List<VivoxOutputDevice>();

		foreach (var device in VivoxService.Instance.AvailableOutputDevices)
		{
			if (device.DeviceID == DefaultCommunicationDeviceId)
			{
				continue;
			}

			if (device.DeviceID == DefaultSystemDeviceId)
			{
				result.Insert(0, device);
			}
			else
			{
				result.Add(device);
			}
		}

		return result;
	}

	private async UniTask SelectDefaultCommunicationDevicesAsync()
	{
		var inputDevices = GetSelectableInputDevices();
		var outputDevices = GetSelectableOutputDevices();

		int inputIndex = FindInputDeviceIndex(inputDevices, DefaultCommunicationDeviceId);
		if (inputDevices.Count > 0 && inputDevices[inputIndex].DeviceID == DefaultCommunicationDeviceId)
		{
			await VivoxService.Instance.SetActiveInputDeviceAsync(inputDevices[inputIndex]);
		}

		int outputIndex = FindOutputDeviceIndex(outputDevices, DefaultCommunicationDeviceId);
		if (outputDevices.Count > 0 && outputDevices[outputIndex].DeviceID == DefaultCommunicationDeviceId)
		{
			await VivoxService.Instance.SetActiveOutputDeviceAsync(outputDevices[outputIndex]);
		}
	}

	private void SubscribeAudioDeviceEvents()
	{
		VivoxService.Instance.AvailableInputDevicesChanged -= OnAudioDevicesChanged;
		VivoxService.Instance.AvailableOutputDevicesChanged -= OnAudioDevicesChanged;
		VivoxService.Instance.AvailableInputDevicesChanged += OnAudioDevicesChanged;
		VivoxService.Instance.AvailableOutputDevicesChanged += OnAudioDevicesChanged;
	}

	private void OnAudioDevicesChanged()
	{
		AudioDevicesChanged?.Invoke();
	}

	private void OnDestroy()
	{
		if (Instance != this)
		{
			return;
		}

		if (VivoxService.Instance != null)
		{
			VivoxService.Instance.AvailableInputDevicesChanged -= OnAudioDevicesChanged;
			VivoxService.Instance.AvailableOutputDevicesChanged -= OnAudioDevicesChanged;
		}

		Instance = null;
	}

	public void ToggleMicTest()
	{
		ToggleMicTestAsync().Forget();
	}

	public void StopMicTest()
	{
		if (_isMicTesting)
		{
			StopMicTestAsync().Forget();
		}
	}

	private async UniTask ToggleMicTestAsync()
	{
		if (_isChangingMicTest)
		{
			return;
		}

		_isChangingMicTest = true;

		try
		{
			if (_isMicTesting)
			{
				await StopMicTestAsync();
			}
			else
			{
				await StartMicTestAsync();
			}
		}
		finally
		{
			_isChangingMicTest = false;
		}
	}

	private async UniTask StartMicTestAsync()
	{
		await LoginTask;

		var service = VivoxService.Instance;

		_micTestChannelName = $"Mic-Test-{service.SignedInPlayerId}";
		_inputWasMuted = service.IsInputDeviceMuted;
		_outputWasMuted = service.IsOutputDeviceMuted;

		// 마이크 테스트용 채널 참가 시, 마이크 테스트용 채널에서는 내 목소리만 들리도록 설정
		await service.JoinEchoChannelAsync(_micTestChannelName, ChatCapability.AudioOnly);
		await service.SetChannelTransmissionModeAsync(TransmissionMode.Single, _micTestChannelName);

		service.UnmuteInputDevice();
		service.UnmuteOutputDevice();

		_isMicTesting = true;
		MicTestStateChanged?.Invoke(true);
		Debug.Log($"[Vivox] 마이크 테스트 시작");
	}

	private async UniTask StopMicTestAsync()
	{
		if (!_isMicTesting)
		{
			return;
		}

		var service = VivoxService.Instance;

		// 채널 전환 순간 게임 채널로 소리가 새지 않게 잠시 음소거
		service.MuteInputDevice();

		// 마이크 테스트용 채널에서 세션 채널로 전환 시, 세션 채널에서는 내 목소리가 들리지 않도록 설정
		if (!string.IsNullOrEmpty(_sessionChannelName) && service.ActiveChannels.ContainsKey(_sessionChannelName))
		{
			await service.SetChannelTransmissionModeAsync(TransmissionMode.Single, _sessionChannelName);
		}
		else
		{
			await service.SetChannelTransmissionModeAsync(TransmissionMode.None);
		}

		if (service.ActiveChannels.ContainsKey(_micTestChannelName))
		{
			await service.LeaveChannelAsync(_micTestChannelName);
		}

		//--- 마이크 테스트 종료 시, 원래 마이크가 음소거 상태가 아니었다면 마이크를 다시 켠다. ---//
		if (!_inputWasMuted)
		{
			service.UnmuteInputDevice();
		}

		if (_outputWasMuted)
		{
			service.MuteOutputDevice();
		}

		_micTestChannelName = null;
		_isMicTesting = false;
		MicTestStateChanged?.Invoke(false);

		Debug.Log("[Vivox] 마이크 테스트 종료");
	}

}
