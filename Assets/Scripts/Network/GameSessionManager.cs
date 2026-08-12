using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
	private string _pendingLeaveReason;

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
			await CleanUpFailedSessionAsync();
			OnSessionError?.Invoke(ToUserMessage(e));
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
			// 죽은 방은 로비 레코드가 TTL 동안 남아 있어 조인 자체는 통과한다. 실제 연결이 섰는지 여기서 확인한다.
			if (!NetworkManager.Singleton.IsListening)
			{
				throw new SessionException(
					"Joined the lobby record but the network client never started (host is gone).",
					SessionError.NetworkManagerStartFailed,
					null);
			}

            SubscribeSceneEvents();
			OnSessionJoined?.Invoke();
		}
		catch (Exception e)
		{
			Debug.LogError($"[GameSessionManager] 세션 참가 중 '{stage}' 단계에서 오류가 발생했습니다.\n오류 내용: {e.Message}");
			await CleanUpFailedSessionAsync();
			OnSessionError?.Invoke(ToUserMessage(e));
		}
	}

	// 조인이 통과한 뒤 실패하면 CurrentSession이 남아 로비에 유령 참가자로 걸린다.
	// 다음 방 생성/참가가 정상 동작하도록 여기서 정리한다.
	private async Task CleanUpFailedSessionAsync()
	{
		if (CurrentSession == null) return;

		try
		{
			await CurrentSession.LeaveAsync();
		}
		catch (Exception e)
		{
			// 이미 사라진 세션이면 나가기도 실패하는데, 참조만 끊으면 되므로 로그만 남긴다.
			Debug.LogWarning($"[GameSessionManager] 실패한 세션 정리 중 오류: {e.Message}");
		}

		CurrentSession = null;
	}

	// Unity Services 예외 메시지는 영문 원문이라 그대로 띄우면 알아볼 수 없어 한글 문구로 바꿔준다.
	// (원문은 호출부의 Debug.LogError에 그대로 남는다)
	private static string ToUserMessage(Exception e)
	{
		const string defaultMessage = "네트워크 오류로 연결하지 못했습니다";

		if (e is not SessionException sessionException) return defaultMessage;

		switch (sessionException.Error)
		{
			case SessionError.SessionNotFound:
			case SessionError.SessionDeleted:
			case SessionError.NetworkManagerStartFailed:
			case SessionError.NetworkSetupFailed:
				return "방 코드를 다시 확인해주세요";
			case SessionError.RateLimitExceeded:
				return "요청이 너무 잦습니다. 잠시 후 다시 시도해주세요";
			default:
				// 정원 초과는 별도 SessionError 없이 Unknown으로 넘어와 메시지로만 구분할 수 있다.
				return sessionException.Message != null &&
				       sessionException.Message.Contains("full", StringComparison.OrdinalIgnoreCase)
					? "방 정원이 가득 찼습니다"
					: defaultMessage;
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

            // PlayerMoveSample이 있으면 항상 Teleport, PlayerHealth가 있으면 Reset만 추가로 수행합니다.
            PlayerMoveSample player = null;
            PlayerHealth playerHealth = null;

            playerObject.TryGetComponent(out player);
            playerObject.TryGetComponent(out playerHealth);

            if (player != null)
            {
                if (playerHealth != null)
                {
                    // 서버/네트워크 권한이 필요하면 여기서 검증하거나 서버-side 초기화로 옮기세요.
                    playerHealth.ResetForNewRound();
                }

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

		LastLeaveReason = ResolveLeaveReason();
		_pendingLeaveReason = null;
		_isLeavingVoluntarily = false;

        VivoxManager.Instance.LeaveSessionChannel();
        CurrentSession = null;
		SceneManager.LoadScene(_lobbySceneName);
	}

	// 내가 사유를 지정하고 나간 경우가 아니면 기존 기본 문구를 쓴다.
	private string ResolveLeaveReason()
	{
		if (!string.IsNullOrEmpty(_pendingLeaveReason)) return _pendingLeaveReason;

		// NGO는 서버가 사유를 보내지 않았을 때도 "[Disconnect Event]..." 형태의 디버그 문자열을 채워둔다.
		// 대괄호로 시작하지 않는 경우만 서버가 실제로 보낸 사유로 취급한다.
		string disconnectReason = NetworkManager.Singleton.DisconnectReason;
		if (!string.IsNullOrEmpty(disconnectReason) && !disconnectReason.StartsWith("["))
		{
			return disconnectReason;
		}

		if (_isLeavingVoluntarily) return "방을 나왔습니다";
		return NetworkManager.Singleton.IsHost ? "연결이 끊겼습니다" : "호스트가 방을 나갔습니다";
	}

	public async void LeaveSession()
	{
		if (CurrentSession == null) return;

		_isLeavingVoluntarily = true;
		await CurrentSession.LeaveAsync();
	}

	// 타임아웃/오류로 클라이언트가 스스로 나갈 때, 로비에 표시할 사유를 지정해 퇴장한다.
	public void LeaveSessionWithReason(string reason)
	{
		_pendingLeaveReason = reason;
		LeaveSession();
	}

	// 방장이 대기방에서 "게임 시작"을 눌렀을 때 호출한다.
	public void StartGame()
	{
		if (!NetworkManager.Singleton.IsServer) return;

		NetworkManager.Singleton.SceneManager.LoadScene(_gameSceneName, LoadSceneMode.Single);
	}
}
