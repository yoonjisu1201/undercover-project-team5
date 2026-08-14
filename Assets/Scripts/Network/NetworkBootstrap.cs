using System;
using System.Text;
using Cysharp.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

// Unity Gaming Services 초기화 + 익명 로그인을 앱 시작 시 한 번만 수행한다.

public class NetworkBootstrap : MonoBehaviour
{
	private const int MaxProfileLength = 30;
	private const string AuthProfileEnvironmentVariable = "UC_AUTH_PROFILE";

	public static bool IsSignedIn => AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn;

	// 세션 생성/참가 전에 이 Task를 먼저 기다리면, 로그인 완료 시점과 무관하게 항상 안전하다.
	// Preserve()로 여러 곳에서 반복 await 가능하게 만든다.
	public static UniTask SignInTask { get; private set; }

	private static NetworkBootstrap s_instance;

	private void Awake()
	{
		if (s_instance != null && s_instance != this)
		{
			Destroy(gameObject);
			return;
		}
		s_instance = this;

		DontDestroyOnLoad(gameObject);
		SignInTask = SignInAsync().Preserve();
	}

	private async UniTask SignInAsync()
	{
		string authProfile = ResolveAuthenticationProfile();

		if (UnityServices.State == ServicesInitializationState.Uninitialized)
		{
			await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(authProfile));
		}
		else if (!AuthenticationService.Instance.IsSignedIn && AuthenticationService.Instance.Profile != authProfile)
		{
			AuthenticationService.Instance.SwitchProfile(authProfile);
		}

		if (!AuthenticationService.Instance.IsSignedIn)
		{
			await AuthenticationService.Instance.SignInAnonymouslyAsync();
		}

		Debug.Log($"UGS 로그인 완료: profile={AuthenticationService.Instance.Profile}, playerId={AuthenticationService.Instance.PlayerId}");
	}

	private static string ResolveAuthenticationProfile()
	{
		string explicitProfile = GetCommandLineProfile();
		if (!string.IsNullOrEmpty(explicitProfile)) return SanitizeProfile(explicitProfile);

		string environmentProfile = Environment.GetEnvironmentVariable(AuthProfileEnvironmentVariable);
		if (!string.IsNullOrEmpty(environmentProfile)) return SanitizeProfile(environmentProfile);

		string projectHash = StableHash(Application.dataPath).ToString("x8");
		int processId = System.Diagnostics.Process.GetCurrentProcess().Id;
		return SanitizeProfile($"uc{projectHash}_{processId}");
	}

	private static string GetCommandLineProfile()
	{
		string[] args = Environment.GetCommandLineArgs();
		for (int i = 0; i < args.Length; i++)
		{
			if ((args[i] == "-authProfile" || args[i] == "--auth-profile") && i + 1 < args.Length)
			{
				return args[i + 1];
			}
		}

		return null;
	}

	private static string SanitizeProfile(string profile)
	{
		var builder = new StringBuilder(MaxProfileLength);
		foreach (char c in profile)
		{
			if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
			{
				builder.Append(c);
			}
			else
			{
				builder.Append('_');
			}

			if (builder.Length == MaxProfileLength) break;
		}

		return builder.Length == 0 ? "undercover" : builder.ToString();
	}

	private static uint StableHash(string value)
	{
		const uint offsetBasis = 2166136261;
		const uint prime = 16777619;

		uint hash = offsetBasis;
		foreach (char c in value)
		{
			hash ^= c;
			hash *= prime;
		}

		return hash;
	}
}
