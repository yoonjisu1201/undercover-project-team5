using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 세션 생성/조인 대기 중 로딩 오버레이를 표시한다.
// 세션 생성/조인 네트워크 요청 구간은 실제 진행률이 없어 시간 기반으로 절반까지 채우고,
// 실제 씬 로드가 시작되면 그 지점부터 AsyncOperation.progress를 이어서 반영한다.
// 씬이 바뀌어도(로비 → 방) 유지돼야 하므로 DontDestroyOnLoad로 만든다.
public class LoadingOverlayUI : MonoBehaviour
{
	private static LoadingOverlayUI s_instance; // 중복 방지용, 외부에서 참조하지 않음

	[SerializeField] private GameObject _overlayPanel;
	[SerializeField] private Slider _progressSlider;
	[SerializeField] private string _waitingRoomSceneName = "WaitingRoom";

	[Header("세션 요청 대기 연출 (실제 진행률 없음)")]
	[SerializeField] private float _preLoadFillDuration = 3f; // 0 → _preLoadFillCap까지 걸리는 시간
	[SerializeField] private float _preLoadFillCap = 0.7f;    // 씬 로드 시작 전까지는 이 값을 넘지 않음

	private CancellationTokenSource _cts;

	private void Awake()
	{
		if (s_instance != null && s_instance != this)
		{
			Destroy(gameObject);
			return;
		}
		s_instance = this;

		// 이 컨트롤러는 로비 Canvas의 자식으로 배치돼 있다. 오버레이만 씬 전환 후에도 유지되도록
		// 자체 루트 Canvas로 분리한 뒤 DontDestroyOnLoad를 적용한다.
		transform.SetParent(null, false);
		Canvas canvas = gameObject.GetComponent<Canvas>();
		if (canvas == null)
		{
			canvas = gameObject.AddComponent<Canvas>();
		}
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.overrideSorting = true;
		canvas.sortingOrder = short.MaxValue;

		if (gameObject.GetComponent<GraphicRaycaster>() == null)
		{
			gameObject.AddComponent<GraphicRaycaster>();
		}

		DontDestroyOnLoad(gameObject);
	}

	private void Start()
	{
		GameSessionManager.Instance.OnSessionStarting += Show;
		GameSessionManager.Instance.OnSessionError += HandleSessionError;
		GameSessionManager.Instance.OnWaitingRoomSceneLoadStarted += HandleSceneLoadStarted;
		GameSessionManager.Instance.OnSessionJoined += HandleSessionJoined;
		GameSessionManager.Instance.OnSessionCreated += HandleSessionCreated;
		SceneManager.sceneLoaded += HandleSceneLoaded;

		Hide();
	}

	private void OnDestroy()
	{
		if (GameSessionManager.Instance != null)
		{
			GameSessionManager.Instance.OnSessionStarting -= Show;
			GameSessionManager.Instance.OnSessionError -= HandleSessionError;
			GameSessionManager.Instance.OnWaitingRoomSceneLoadStarted -= HandleSceneLoadStarted;
			GameSessionManager.Instance.OnSessionJoined -= HandleSessionJoined;
			GameSessionManager.Instance.OnSessionCreated -= HandleSessionCreated;
		}

		SceneManager.sceneLoaded -= HandleSceneLoaded;
		_cts?.Cancel();
		_cts?.Dispose();
	}

	private void Show()
	{
		_overlayPanel.SetActive(true);
		_progressSlider.value = 0f;

		_cts?.Cancel();
		_cts?.Dispose();
		_cts = new CancellationTokenSource();
		FillWhileWaitingAsync(_cts.Token).Forget();
	}

	private void Hide()
	{
		_overlayPanel.SetActive(false);

		_cts?.Cancel();
		_cts?.Dispose();
		_cts = null;
	}

	// 세션 생성/조인 요청 중(진행률 데이터가 없는 구간)엔 시간 기반으로 _preLoadFillCap까지만 채운다.
	private async UniTaskVoid FillWhileWaitingAsync(CancellationToken token)
	{
		float elapsed = 0f;

		while (elapsed < _preLoadFillDuration)
		{
			elapsed += Time.deltaTime;
			_progressSlider.value = Mathf.Lerp(0f, _preLoadFillCap, elapsed / _preLoadFillDuration);
			await UniTask.Yield(PlayerLoopTiming.Update, token);
		}

		_progressSlider.value = _preLoadFillCap;
	}

	private void HandleSceneLoadStarted(AsyncOperation asyncOperation)
	{
		_cts?.Cancel();
		_cts?.Dispose();
		_cts = new CancellationTokenSource();
		TrackRealProgressAsync(asyncOperation, _cts.Token).Forget();
	}

	private void HandleSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
	{
		if (scene.name != _waitingRoomSceneName) return;

		_progressSlider.value = 1f;
		Hide();
	}

	private void HandleSessionJoined()
	{
		HideIfAlreadyInWaitingRoom();
	}

	private void HandleSessionCreated(string _)
	{
		HideIfAlreadyInWaitingRoom();
	}

	private void HideIfAlreadyInWaitingRoom()
	{
		if (SceneManager.GetActiveScene().name != _waitingRoomSceneName) return;

		_progressSlider.value = 1f;
		Hide();
	}

	// 씬 로드가 시작된 시점의 슬라이더 값부터 1까지, 실제 진행률에 맞춰 이어서 채운다.
	private async UniTaskVoid TrackRealProgressAsync(AsyncOperation asyncOperation, CancellationToken token)
	{
		float startValue = _progressSlider.value;

		while (!asyncOperation.isDone)
		{
			_progressSlider.value = Mathf.Lerp(startValue, 1f, asyncOperation.progress);
			await UniTask.Yield(PlayerLoopTiming.Update, token);
		}

		_progressSlider.value = 1f;
	}

	private void HandleSessionError(string message)
	{
		Hide();
	}
}
