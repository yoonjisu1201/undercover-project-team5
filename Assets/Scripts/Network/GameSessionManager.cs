using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

/* 조인코드로만 입장 가능한 세션(방)을 만들고 참가하는 기능.
 * 랜덤 매칭, 빠른 시작(QuickJoin), 공개 세션 목록 조회는 사용하지 않는다.
 */
public class GameSessionManager : MonoBehaviour
{
	public static GameSessionManager Instance { get; private set; }

	[Header("세션 설정")]
	[SerializeField] private int _maxPlayers = 4;
	[FormerlySerializedAs("_gameplaySceneName")]
	[SerializeField] private string _waitingRoomSceneName = "WaitingRoom";
	[SerializeField] private string _lobbySceneName = "Lobby2";
	[FormerlySerializedAs("_roundSceneName")]
	[SerializeField] private string _gameSceneName = "GameScene";

	public ISession CurrentSession { get; private set; }
	public string JoinCode => CurrentSession?.Code;
	public string LastLeaveReason { get; set; }

	public event Action<string> OnSessionCreated; // 조인코드 발급 완료
	public event Action OnSessionJoined;          // 조인코드로 참가 완료
	public event Action<string> OnSessionError;   // 실패 사유 전달
	public event Action OnSessionStarting;                            // 세션 생성/참가 시도 시작
	public event Action<AsyncOperation> OnWaitingRoomSceneLoadStarted; // 내 로컬 씬 로딩이 시작됨 (진행률 포함)

	private bool _isLeavingVoluntarily;

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

	private void OnDestroy()
	{
		if (NetworkManager.Singleton == null) return;

		NetworkManager.Singleton.ConnectionApprovalCallback = null;

		if (NetworkManager.Singleton.SceneManager == null) return;

		NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleWaitingRoomSceneLoaded;
		NetworkManager.Singleton.SceneManager.OnSynchronizeComplete -= HandleClientSynchronized;
		NetworkManager.Singleton.SceneManager.OnLoad -= HandleWaitingRoomSceneLoadStarted;
		NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
	}

	// 호스트: 방을 만들고 조인코드를 발급받는다.
	public async void CreateSession()
	{
		OnSessionStarting?.Invoke();
		string stage = "로그인 대기";
		try
		{
			await NetworkBootstrap.SignInTask; // 로그인 끝날 때까지 대기

			stage = "연결 승인 설정";
			PrepareConnectionApproval();

			var options = new SessionOptions
			{
				MaxPlayers = _maxPlayers,
				IsPrivate = true
			}.WithRelayNetwork();

			stage = "세션 생성 요청";
			CurrentSession = await MultiplayerService.Instance.CreateSessionAsync(options);

			stage = "음성 채널 참가";
			VivoxManager.Instance.JoinSessionChannel(CurrentSession.Code);

			stage = "씬 이벤트 구독";
			SubscribeSceneEvents();
			OnSessionCreated?.Invoke(CurrentSession.Code);

			if (NetworkManager.Singleton.IsServer)
			{
				stage = "대기방 씬 로드";
				NetworkManager.Singleton.SceneManager.LoadScene(_waitingRoomSceneName, LoadSceneMode.Single);
			}
		}
		catch (Exception e)
		{
			Debug.LogError($"[GameSessionManager] 세션 생성 중 '{stage}' 단계에서 오류가 발생했습니다.\n오류 내용: {e.Message}");
			OnSessionError?.Invoke(e.Message);
		}
	}

	// 클라이언트: 조인코드로 방에 참가한다.
	public async void JoinSessionByCode(string joinCode)
	{
		OnSessionStarting?.Invoke();
		string stage = "로그인 대기";
		try
		{
			await NetworkBootstrap.SignInTask; // 로그인 끝날 때까지 대기

			stage = "연결 승인 설정";
			PrepareConnectionApproval();

			stage = "조인코드로 세션 참가 요청";
			CurrentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode);

			stage = "음성 채널 참가";
            VivoxManager.Instance.JoinSessionChannel(CurrentSession.Code);

			stage = "씬 이벤트 구독";
            SubscribeSceneEvents();
			OnSessionJoined?.Invoke();
		}
		catch (Exception e)
		{
			Debug.LogError($"[GameSessionManager] 세션 참가 중 '{stage}' 단계에서 오류가 발생했습니다.\n오류 내용: {e.Message}");
			OnSessionError?.Invoke(e.Message);
		}
	}

	// 로비 씬에는 바닥이 없어 접속 즉시 자동 스폰되면 캐릭터가 떨어진다.
	// 접속 승인 방식은 NetworkManager가 시작(StartHost/StartClient)되기 전에 설정해야 하므로
	// 세션 생성/참가보다 먼저 호출한다.
	private void PrepareConnectionApproval()
	{
		var networkManager = NetworkManager.Singleton;
		networkManager.NetworkConfig.ConnectionApproval = true;
		networkManager.ConnectionApprovalCallback = HandleConnectionApproval;
	}

    // NetworkManager.SceneManager는 시작된 후에 생성되므로, 세션 생성/참가가 끝난 뒤에 구독해야 한다.
    // 대기방 씬 로드/동기화가 완료되면 서버가 직접 스폰한다.
    private void SubscribeSceneEvents()
    {
        UnsubscribeSceneEvents(); // 중복 구독 방지 (기존 -= += 와 동일한 멱등성)
        var networkManager = NetworkManager.Singleton;
        networkManager.SceneManager.OnLoadEventCompleted += HandleWaitingRoomSceneLoaded;
        networkManager.SceneManager.OnLoadEventCompleted += HandleGameSceneLoaded;
        networkManager.SceneManager.OnSynchronizeComplete += HandleClientSynchronized;
        networkManager.SceneManager.OnLoad += HandleWaitingRoomSceneLoadStarted;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }
    private void UnsubscribeSceneEvents()
    {
        var networkManager = NetworkManager.Singleton;
        if (networkManager == null) return;
        if (networkManager.SceneManager != null)
        {
            networkManager.SceneManager.OnLoadEventCompleted -= HandleWaitingRoomSceneLoaded;
            networkManager.SceneManager.OnLoadEventCompleted -= HandleGameSceneLoaded;
            networkManager.SceneManager.OnSynchronizeComplete -= HandleClientSynchronized;
            networkManager.SceneManager.OnLoad -= HandleWaitingRoomSceneLoadStarted;
        }
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void HandleWaitingRoomSceneLoadStarted(ulong clientId, string sceneName, LoadSceneMode loadSceneMode, AsyncOperation asyncOperation)
	{
		if (sceneName != _waitingRoomSceneName || clientId != NetworkManager.Singleton.LocalClientId) return;

		OnWaitingRoomSceneLoadStarted?.Invoke(asyncOperation);
	}

	private void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
	{
		response.Approved = true;
		response.CreatePlayerObject = false;
	}

	private void HandleWaitingRoomSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
	{
		if (sceneName != _waitingRoomSceneName || !NetworkManager.Singleton.IsServer) return;

		foreach (var clientId in clientsCompleted)
		{
			var playerObject = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;
			if (playerObject == null)
			{
				SpawnPlayerForClient(clientId);
			}
			else if (playerObject.TryGetComponent(out PlayerHealth playerHealth) &&
				playerObject.TryGetComponent(out PlayerMoveSample player))
			{
				playerHealth.ResetForNewRound();
				player.TeleportToPosition(Vector3.zero, playerObject.transform.rotation);
			}
		}
	}

	// 플레이어 오브젝트는 대기방(WaitingRoom)에서 스폰된 채로 게임씬(PlayScene) 전환에도 파괴되지 않고 그대로 유지된다.
	// 따라서 InteractionPromptUI/InventoryUI를 찾지 못하게 되는데, UI가 존재하는 게임 씬 로드가 끝난 시점에
	// PlayerInteraction.InitializeOnGameScene()/PlayerInventory.InitializeOnGameScene()를 호출해 UI를 바인딩하게 한다.
	private void HandleGameSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
	{
		if (sceneName != _gameSceneName) return;

		// 플레이어 각자가 자기 자신의 InteractionPromptUI/InventoryUI 초기화 및 바인딩
		var localPlayerObject = NetworkManager.Singleton.LocalClient?.PlayerObject;
		if (localPlayerObject == null) {
			Debug.LogError($"[GameSessionManger] 초기화 중 localPlayerObject 발견하지 못함.");
			return;
		}

		if (localPlayerObject.TryGetComponent(out PlayerInteraction localPlayerInteraction)) {
			localPlayerInteraction.InitializeOnGameScene();
		}

		if (localPlayerObject.TryGetComponent(out PlayerInventory localPlayerInventory)) {
			localPlayerInventory.InitializeOnGameScene();
		}
	}

	private void HandleClientSynchronized(ulong clientId)
	{
		if (!NetworkManager.Singleton.IsServer) return;
		if (SceneManager.GetActiveScene().name != _waitingRoomSceneName) return;

		SpawnPlayerForClient(clientId);
	}

	private void SpawnPlayerForClient(ulong clientId)
	{
		if (NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject != null) return;

		var playerInstance = Instantiate(NetworkManager.Singleton.NetworkConfig.PlayerPrefab);
		playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
	}

	// 내 연결이 끊긴 경우에만 로비로 돌아간다 (자진 퇴장/호스트가 나가서 강제로 끊긴 경우 모두 포함).
	private void HandleClientDisconnected(ulong clientId)
	{
		if (clientId != NetworkManager.Singleton.LocalClientId) return;

		LastLeaveReason = _isLeavingVoluntarily
			? "방을 나왔습니다"
			: NetworkManager.Singleton.IsHost
				? "연결이 끊겼습니다"
				: "호스트가 방을 나갔습니다";
		_isLeavingVoluntarily = false;

        VivoxManager.Instance.LeaveSessionChannel();
        CurrentSession = null;
		SceneManager.LoadScene(_lobbySceneName);
	}

	public async void LeaveSession()
	{
		if (CurrentSession == null) return;

		_isLeavingVoluntarily = true;
		await CurrentSession.LeaveAsync();
	}

	// 방장이 대기방에서 "게임 시작"을 눌렀을 때 호출한다.
	public void StartGame()
	{
		if (!NetworkManager.Singleton.IsServer) return;

		NetworkManager.Singleton.SceneManager.LoadScene(_gameSceneName, LoadSceneMode.Single);
	}
}
