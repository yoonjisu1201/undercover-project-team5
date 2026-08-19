using Cysharp.Threading.Tasks;
using System;
using System.Threading.Tasks;
using Unity.Services.Vivox;
using UnityEngine;

// Vivox 음성 서비스 초기화 + 로그인을 앱 시작 시 한 번만 수행하고,
// 게임(세션) 음성 채널 참가와 마이크 테스트를 조정한다.
// NetworkBootstrap의 UGS 로그인이 끝난 뒤에 실행되어야 한다.
//
// 장치 목록 규칙은 VivoxAudioDevices, 마이크 소리 되들려주기는 MicMonitor가 담당한다.
public class VivoxManager : MonoBehaviour
{
	// 이 음량(0~1) 이상이면 말하는 중으로 본다. 숨소리나 잡음으로 아이콘이 깜빡이지 않을 정도로만 잡는다.
	private const double SpeakingEnergyThreshold = 0.02;

	public static VivoxManager Instance { get; private set; }

	public static bool IsLoggedIn => VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn;
	public static bool IsMicMuted => VivoxService.Instance != null && VivoxService.Instance.IsInputDeviceMuted;
	public static bool IsOutputMuted => VivoxService.Instance != null && VivoxService.Instance.IsOutputDeviceMuted;

	// 음성 채널 참가 전에 이 Task를 먼저 기다리면, 로그인 완료 시점과 무관하게 항상 안전하다.
	// UniTask는 Preserve()를 해도 "완료 후 재await"만 되고 "대기 중 동시 await"는 예외가 난다.
	// 채널 참가·장치 변경·마이크 테스트가 로그인 도중 겹칠 수 있으므로 Task를 쓴다.
	public static Task LoginTask { get; private set; }

	private string _sessionChannelName; // 실제 플레이 음성채널

	private bool _isMicTesting;
	private bool _isChangingMicTest; // 시작/종료 전환이 진행 중인지

	// 마이크 테스트 전의 장치 상태. 테스트가 끝나면 이대로 되돌린다.
	private bool _inputWasMuted;
	private bool _outputWasMuted;

	private MicMonitor _micMonitor;

	public bool IsMicTesting => _isMicTesting;

	// 팀원 입장에서 내 목소리가 들리지 않는 상태.
	// 직접 마이크를 껐거나, 마이크 테스트 중이라 게임 채널로 전송되지 않는 경우다.
	// (뮤트 버튼 UI는 장치 자체의 상태인 IsMicMuted를 쓴다.)
	public bool IsMutedForSessionChannel => IsMicMuted || _isMicTesting;

	// 레벨 막대에 쓸 현재 마이크 음량(0~1). 테스트 중이 아니면 0이다.
	public float MicMonitorEnergy01 => _micMonitor != null ? _micMonitor.Energy01 : 0f;

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

		_micMonitor = new MicMonitor(gameObject);
	}

	private void Start()
	{
		LoginTask = LoginAsync();
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

	//--- 로그인 ---//

	private async Task LoginAsync()
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

				await VivoxAudioDevices.SelectDefaultCommunicationDevicesAsync();
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

	//--- 게임 음성 채널 ---//

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

			// 전송 모드는 로그인 세션 단위 설정이라 채널 참가만으로는 복구되지 않는다.
			// 이전에 None으로 남았다면 마이크가 죽은 상태이므로, 참가할 때마다 되돌린다.
			await RestoreSessionTransmissionAsync();

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

			_sessionChannelName = null;
			_isMicTesting = false;
		}
		catch (Exception e)
		{
			Debug.LogError($"[Vivox] 채널 나가기 실패: {e.Message}");
		}
	}

	// 내 목소리를 게임 채널로만 내보낸다.
	private async UniTask RestoreSessionTransmissionAsync()
	{
		var service = VivoxService.Instance;

		if (string.IsNullOrEmpty(_sessionChannelName) || !service.ActiveChannels.ContainsKey(_sessionChannelName))
		{
			// 아직 게임 채널이 없으면 내보낼 곳이 없다. 방에 들어갈 때 다시 세팅한다.
			await service.SetChannelTransmissionModeAsync(TransmissionMode.None);
			return;
		}

		await service.SetChannelTransmissionModeAsync(TransmissionMode.Single, _sessionChannelName);

		// 실제로 복구됐는지 확인한다. 여기가 비어 있으면 내 목소리가 아무에게도 가지 않는 상태다.
		if (service.TransmittingChannels.Count == 0)
		{
			Debug.LogError($"[Vivox] 전송 채널 복구 실패. 게임 채널: {_sessionChannelName}, 참가 중 채널: {string.Join(", ", service.ActiveChannels.Keys)}");
		}
	}

	//--- 말하는 중 판정 ---//

	// 내가 지금 게임 채널로 말하고 있는지.
	// 남이 말하는지도 Vivox로 볼 수는 있지만, 그 참가자가 화면의 어느 플레이어인지 잇는 게 ID 문자열 일치에
	// 의존해 취약하고 오디오가 실제로 도달해야만 판정된다. 그래서 각자 자기 상태만 보고 Player가 공유한다.
	public bool IsLocalSpeaking
	{
		get
		{
			var service = VivoxService.Instance;

			// 테스트 중에는 게임 채널로 전송되지 않으므로 팀원에게 들리지 않는다. 아이콘도 띄우지 않는다.
			if (service == null || !service.IsLoggedIn || IsMicMuted || _isMicTesting
				|| string.IsNullOrEmpty(_sessionChannelName))
			{
				return false;
			}

			if (!service.ActiveChannels.TryGetValue(_sessionChannelName, out var participants))
			{
				return false;
			}

			foreach (var participant in participants)
			{
				if (participant.IsSelf)
				{
					// SpeechDetected는 Vivox의 VAD 결과다. 안 올라오는 경우가 있어 실제 음량도 함께 본다.
					return participant.SpeechDetected || participant.AudioEnergy >= SpeakingEnergyThreshold;
				}
			}

			return false;
		}
	}

	//--- 뮤트 토글 (버튼에서 직접 호출) ---//

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

	//--- 장치 선택 ---//

	public async UniTask SelectInputDeviceAsync(int direction)
	{
		await LoginTask;

		if (await VivoxAudioDevices.SelectNextInputAsync(direction))
		{
			AudioDevicesChanged?.Invoke();
		}
	}

	public async UniTask SelectOutputDeviceAsync(int direction)
	{
		await LoginTask;

		if (await VivoxAudioDevices.SelectNextOutputAsync(direction))
		{
			AudioDevicesChanged?.Invoke();
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

	//--- 마이크 테스트 ---//
	// 테스트 중에는 게임 채널로 전송하지 않고(= 팀원에게 뮤트), 마이크 입력을 로컬에서 되들려준다.

	public void ToggleMicTest()
	{
		// 게임 채널 차단은 여기서 곧바로 한다. 아래 비동기 처리를 기다리면 그 사이 동안
		// 내 목소리가 팀원에게 그대로 들린다. MuteInputDevice는 동기 호출이라 즉시 끊긴다.
		if (!_isMicTesting && VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
		{
			_inputWasMuted = VivoxService.Instance.IsInputDeviceMuted;
			_outputWasMuted = VivoxService.Instance.IsOutputDeviceMuted;

			VivoxService.Instance.MuteInputDevice();
			VivoxService.Instance.UnmuteOutputDevice();  // 내 목소리를 들어야 하므로 스피커는 켠다
		}

		ToggleMicTestAsync().Forget();
	}

	public void StopMicTest()
	{
		StopMicTestRequestAsync().Forget();
	}

	// 설정 창을 닫는 경로는 버튼 토글과 달리 언제든 들어올 수 있다.
	// 시작이 진행 중인데 종료가 끼어들면 뒤늦게 도착한 시작 처리가 상태를 되살려 버리므로,
	// 진행 중인 전환이 끝나기를 기다린다.
	private async UniTask StopMicTestRequestAsync()
	{
		await UniTask.WaitUntil(() => !_isChangingMicTest);

		if (!_isMicTesting)
		{
			return;
		}

		_isChangingMicTest = true;

		try
		{
			await StopMicTestAsync();
		}
		finally
		{
			_isChangingMicTest = false;
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

		// 게임 채널 차단(MuteInputDevice)과 원래 상태 기록은 ToggleMicTest에서 이미 끝냈다.
		// 여기서는 내 목소리를 들려주는 일만 한다.
		_isMicTesting = true;
		MicTestStateChanged?.Invoke(true);

		// 캡처 시작을 기다리는 동안 테스트가 끝났으면 재생하지 않는다.
		await _micMonitor.StartAsync(() => _isMicTesting);

		Debug.Log("[Vivox] 마이크 테스트 시작");
	}

	private async UniTask StopMicTestAsync()
	{
		if (!_isMicTesting)
		{
			return;
		}

		var service = VivoxService.Instance;

		// 상태를 먼저 내려둔다. 아래에서 실패해도 "테스트 중"으로 남아 다음 종료가 막히지 않게 한다.
		_isMicTesting = false;

		try
		{
			_micMonitor.Stop();

			// 전송 대상이 게임 채널을 가리키는지 확인해 둔다. 테스트 중에는 채널을 건드리지 않았지만,
			// 그 사이에 방을 옮겼거나 이전 상태가 남아 있을 수 있다.
			await RestoreSessionTransmissionAsync();
		}
		finally
		{
			// 게임 채널로 복귀한 다음에 장치 상태를 테스트 전으로 되돌린다.
			// 중간에 실패해도 마이크가 꺼진 채로 남지 않도록 finally에서 처리한다.
			if (_inputWasMuted)
			{
				service.MuteInputDevice();
			}
			else
			{
				service.UnmuteInputDevice();
			}

			if (_outputWasMuted)
			{
				service.MuteOutputDevice();
			}
			else
			{
				service.UnmuteOutputDevice();
			}

			MicTestStateChanged?.Invoke(false);
			Debug.Log("[Vivox] 마이크 테스트 종료");
		}
	}
}
