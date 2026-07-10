using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

// Unity Gaming Services 초기화 + 익명 로그인을 앱 시작 시 한 번만 수행한다.

public class NetworkBootstrap : MonoBehaviour
{
	public static bool IsSignedIn => AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn;

	private async void Awake()
	{
		DontDestroyOnLoad(gameObject);

		if (UnityServices.State == ServicesInitializationState.Uninitialized)
		{
			await UnityServices.InitializeAsync();
		}

		if (!AuthenticationService.Instance.IsSignedIn)
		{
			await AuthenticationService.Instance.SignInAnonymouslyAsync();
		}

		Debug.Log($"UGS 로그인 완료: {AuthenticationService.Instance.PlayerId}");
	}
}
