using Cysharp.Threading.Tasks;
using System;
using Unity.Services.Vivox;
using UnityEngine;

// Vivox 음성 서비스 초기화 + 로그인을 앱 시작 시 한 번만 수행한다.
// NetworkBootstrap의 UGS 로그인이 끝난 뒤에 실행되어야 한다.

public class VivoxManager : MonoBehaviour
{
	public static VivoxManager Instance { get; private set; }

	public static bool IsLoggedIn => VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn;
	public static bool IsMicMuted => VivoxService.Instance != null && VivoxService.Instance.IsInputDeviceMuted;
	public static bool IsOutputMuted => VivoxService.Instance != null && VivoxService.Instance.IsOutputDeviceMuted;

	// 음성 채널 참가 전에 이 Task를 먼저 기다리면, 로그인 완료 시점과 무관하게 항상 안전하다.
	// Preserve()로 여러 곳에서 반복 await 가능하게 만든다.
	public static UniTask LoginTask { get; private set; }

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
			Debug.Log($"[Vivox] 채널 참가 완료: {channelName}");
		}
		catch(Exception e)
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
		catch(Exception e)
		{
			Debug.LogError($"[Vivox] 채널 나가기 실패: {e.Message}");
		}
	}

    // 내 마이크(내가 말하는 소리)를 토글한다. 로그인 전이면 아무 동작도 하지 않는다.
    public void ToggleMicMute()
	{
		if(VivoxService.Instance == null || !VivoxService.Instance.IsLoggedIn)
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
}
