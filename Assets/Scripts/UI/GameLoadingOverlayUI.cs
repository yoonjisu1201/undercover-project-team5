using UnityEngine;
using UnityEngine.SceneManagement;

// 게임 시작을 누른 순간부터 게임씬 로딩 화면을 보여준다.
// 게임씬 안의 패널은 씬 로드가 끝난 뒤에야 켜질 수 있어 정작 로딩이 걸리는 구간을 덮지 못한다.
// 그래서 대기방에 두고 씬 전환을 넘긴다.
public class GameLoadingOverlayUI : MonoBehaviour
{
	private static GameLoadingOverlayUI s_instance; // 중복 방지용, 외부에서 참조하지 않음

	[SerializeField] private GameObject _overlayPanel;

	// 라운드 상태를 구독할 시점(게임씬 로드 완료)을 알기 위해 쓴다.
	[SerializeField] private string _gameSceneName = "PlayScene";

	private void Awake()
	{
		if (s_instance != null && s_instance != this)
		{
			Destroy(gameObject);
			return;
		}
		s_instance = this;

		// DontDestroyOnLoad는 루트 오브젝트에만 걸린다. Canvas 자식으로 옮기지 말 것.
		DontDestroyOnLoad(gameObject);
	}

	private void Start()
	{
		// 방장·참가자 모두 이 이벤트를 받는다. 방장은 서버가 LoadScene을 부르는 그 프레임에 발생한다.
		GameSessionManager.Instance.OnGameSceneLoadStarted += HandleGameSceneLoadStarted;
		SceneManager.sceneLoaded += HandleSceneLoaded;

		Hide();
	}

	private void OnDestroy()
	{
		if (GameSessionManager.Instance != null)
		{
			GameSessionManager.Instance.OnGameSceneLoadStarted -= HandleGameSceneLoadStarted;
		}

		if (RoundManager.Instance != null)
		{
			RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
		}

		SceneManager.sceneLoaded -= HandleSceneLoaded;
	}

	private void Show() => _overlayPanel.SetActive(true);

	private void Hide() => _overlayPanel.SetActive(false);

	// 이 패널에는 진행률 표시가 없어 AsyncOperation은 쓰지 않는다.
	private void HandleGameSceneLoadStarted(AsyncOperation asyncOperation) => Show();

	// 씬 로드가 끝나도 NPC·단서 스폰이 남아 있어 여기서 끄면 안 된다.
	// 라운드가 시작될 때까지 유지하기 위해, 이 시점에 RoundManager를 구독한다.
	private void HandleSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
	{
		if (scene.name != _gameSceneName)
		{
			// 라운드 시작 전에 로비·대기방으로 돌아오는 경로(시작 실패, 강제 퇴장 등)에서는
			// 라운드 상태가 오지 않아 끌 계기가 없다. 게임씬을 벗어나면 무조건 내린다.
			Hide();
			return;
		}

		if (RoundManager.Instance == null)
		{
			// 라운드 상태를 들을 수 없으면 언제 꺼야 할지 알 수 없다. 갇히는 것보다 지금 끄는 편이 낫다.
			Hide();
			return;
		}

		RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
	}

	// 스폰이 끝나 라운드가 Waiting을 벗어나는 첫 순간에만 반응하고 구독을 해제한다.
	private void HandleRoundStateChanged(RoundState state)
	{
		if (state == RoundState.Waiting) return;

		RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
		Hide();

		// 로딩 화면이 걷히는 이 순간이 플레이 씬에 실제로 들어선 시점이다. 씬 로드가 끝나는
		// 시점에 내면 아직 검은 화면인 동안 소리만 나서 들어왔다는 느낌이 나지 않는다.
		// 대기실과 같은 소리를 써서 "방에 들어왔다"는 신호를 통일한다.
		SoundManager.Instance?.Play(SoundKey.Room_In);
	}
}
