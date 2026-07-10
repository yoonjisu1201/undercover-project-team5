using Cysharp.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

// Unity Gaming Services 초기화 + 익명 로그인을 앱 시작 시 한 번만 수행한다.

public class NetworkBootstrap : MonoBehaviour
{
	public static bool IsSignedIn => AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn;

	// 세션 생성/참가 전에 이 Task를 먼저 기다리면, 로그인 완료 시점과 무관하게 항상 안전하다.
	// Preserve()로 여러 곳에서 반복 await 가능하게 만든다.
	public static UniTask SignInTask { get; private set; }

	private void Awake()
	{
		DontDestroyOnLoad(gameObject);
		SignInTask = SignInAsync().Preserve();            
	}

	private async UniTask SignInAsync()
	{
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
