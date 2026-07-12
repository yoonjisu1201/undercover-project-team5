using Cysharp.Threading.Tasks;
using Unity.Services.Vivox;
using UnityEngine;

// Vivox 음성 서비스 초기화 + 로그인을 앱 시작 시 한 번만 수행한다.
// NetworkBootstrap의 UGS 로그인이 끝난 뒤에 실행되어야 한다.

public class VivoxManager : MonoBehaviour
{
	public static VivoxManager Instance { get; private set; }

	public static bool IsLoggedIn => VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn;

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

		LoginTask = LoginAsync().Preserve();
	}

	private async UniTask LoginAsync()
	{
		await NetworkBootstrap.SignInTask; // UGS 로그인 완료 후 Vivox 초기화

		await VivoxService.Instance.InitializeAsync();

		if (!VivoxService.Instance.IsLoggedIn)
		{
			await VivoxService.Instance.LoginAsync();
		}

		Debug.Log($"Vivox 로그인 완료: {VivoxService.Instance.SignedInPlayerId}");
	}
}
