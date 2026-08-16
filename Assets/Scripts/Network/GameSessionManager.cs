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

	// 서버가 우리 코드에서 클라이언트를 내보낼 때 사유 앞에 붙이는 표식.
	// NGO가 자동으로 채우는 영문 사유("Client-1 disconnected by server." 등)와 구분하기 위함이다.
	public const string ServerReasonPrefix = "UC|";

	public event Action<string> OnSessionCreated; // 조인코드 발급 완료
	public event Action OnSessionJoined;          // 조인코드로 참가 완료
	public event Action<string> OnSessionError;   // 실패 사유 전달
	public event Action OnSessionStarting;                            // 세션 생성/참가 시도 시작
	public event Action<AsyncOperation> OnWaitingRoomSceneLoadStarted; // 내 로컬 씬 로딩이 시작됨 (진행률 포함)
	public event Action OnWaitingRoomSceneLoadComplete; // 내 로컬 씬 로딩이 완료됨 (진행률 포함)

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
		NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleGameSceneLoaded;
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

			stage = "호스트 연결 확인";
			if (!await WaitUntilNetworkReadyAsync(requireServer: true))
			{
				throw new SessionException(
					$"Created the lobby record but the network host never started. {DescribeNetworkState()}",
					SessionError.NetworkManagerStartFailed,
					null);
			}

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
			Debug.LogError($"[GameSessionManager] 세션 생성 중 '{stage}' 단계에서 오류가 발생했습니다.\n" +
						   $"오류 내용: [{DescribeError(e)}] {e.Message}");
			await ReleaseCurrentSessionAsync();
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

			stage = "연결 확인";
			// 죽은 방은 로비 레코드가 TTL 동안 남아 있어 조인 자체는 통과한다. 실제 연결이 섰는지 여기서 확인한다.
			if (!await WaitUntilNetworkReadyAsync(requireServer: false))
			{
				throw new SessionException(
					$"Joined the lobby record but the network client never connected. {DescribeNetworkState()}",
					SessionError.NetworkManagerStartFailed,
					null);
			}

			stage = "씬 이벤트 구독";
			SubscribeSceneEvents();
			OnSessionJoined?.Invoke();
		}
		catch (Exception e)
		{
			Debug.LogError($"[GameSessionManager] 세션 참가 중 '{stage}' 단계에서 오류가 발생했습니다.\n" +
						   $"오류 내용: [{DescribeError(e)}] {e.Message}");
			await ReleaseCurrentSessionAsync();
			OnSessionError?.Invoke(ToUserMessage(e, isJoinByCode: true));
		}
	}

	// Unity Lobby 멤버십은 Netcode 연결과 별개라, 연결이 끊겨도 로비에는 멤버로 남는다.
	// 명시적으로 나가지 않으면 같은 방 코드로 재참가할 때 SessionConflict
	// ("player is already a member of the lobby")가 난다.
	private async Task ReleaseCurrentSessionAsync()
	{
		if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
		{
			NetworkManager.Singleton.Shutdown();
		}

		// 나가기 요청이 도는 동안 다른 코드가 죽은 세션을 잡지 않도록 참조부터 끊는다.
		var session = CurrentSession;
		CurrentSession = null;
		if (session == null) return;

		try
		{
			await session.LeaveAsync();
		}
		catch (Exception e)
		{
			// 이미 사라진 세션이면 나가기도 실패하는데, 참조는 이미 끊었으므로 로그만 남긴다.
			Debug.LogWarning($"[GameSessionManager] 세션 정리 중 오류: {e.Message}");
		}
	}

	// 실패 원인을 한눈에 구분하기 위해 SessionError 코드(또는 예외 타입)를 함께 남긴다.
	private static string DescribeError(Exception e) =>
		e is SessionException sessionException ? $"SessionError.{sessionException.Error}" : e.GetType().Name;

	// Unity Services 예외 메시지는 영문 원문이라 그대로 띄우면 알아볼 수 없어 한글 문구로 바꿔준다.
	// (원문은 호출부의 Debug.LogError에 그대로 남는다)
	// 참가는 방 생성과 달리 잘못된 방 코드가 압도적으로 흔해서 원인 불명일 때의 기본 문구가 다르다.
	private static string ToUserMessage(Exception e, bool isJoinByCode = false)
	{
		const string networkMessage = "네트워크 오류로 연결하지 못했습니다";
		const string invalidCodeMessage = "방 코드를 다시 확인해주세요";

		// 로그인이나 서비스 초기화 실패는 SessionException이 아니다. 방 코드와 무관한 실패다.
		if (e is not SessionException sessionException) return networkMessage;

		// 서비스는 코드 형식 위반과 네트워크 오류를 둘 다 Unknown으로만 알려줘 구분할 수 없다.
		// 인터넷이 아예 끊긴 상태라면 코드 문제가 아니라고 확실히 말할 수 있다.
		bool isOffline = Application.internetReachability == NetworkReachability.NotReachable;
		string defaultMessage = isJoinByCode && !isOffline ? invalidCodeMessage : networkMessage;

		switch (sessionException.Error)
		{
			case SessionError.SessionNotFound:
			case SessionError.SessionDeleted:
			case SessionError.NetworkManagerStartFailed:
			case SessionError.NetworkSetupFailed:
				return invalidCodeMessage;
			case SessionError.SessionConflict:
				return "이미 같은 플레이어가 이 방에 참가 중입니다";
			case SessionError.RateLimitExceeded:
				return "요청이 너무 잦습니다. 잠시 후 다시 시도해주세요";
			default:
				// 정원 초과는 별도 SessionError 없이 Unknown으로 넘어와 메시지로만 구분할 수 있다.
				return sessionException.Message.Contains("full", StringComparison.OrdinalIgnoreCase)
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

	private static async Task<bool> WaitUntilNetworkReadyAsync(bool requireServer)
	{
		var networkManager = NetworkManager.Singleton;
		if (networkManager == null) return false;

		const int timeoutMilliseconds = 10000;
		const int pollDelayMilliseconds = 100;
		int elapsedMilliseconds = 0;

		while (!IsNetworkReady(networkManager, requireServer) && elapsedMilliseconds < timeoutMilliseconds)
		{
			await Task.Delay(pollDelayMilliseconds);
			elapsedMilliseconds += pollDelayMilliseconds;
		}

		return IsNetworkReady(networkManager, requireServer);
	}

	private static bool IsNetworkReady(NetworkManager networkManager, bool requireServer)
	{
		if (networkManager == null || !networkManager.IsListening) return false;
		return requireServer ? networkManager.IsServer : networkManager.IsConnectedClient;
	}

	private static string DescribeNetworkState()
	{
		var networkManager = NetworkManager.Singleton;
		if (networkManager == null) return "NetworkManager is null.";

		return $"IsListening={networkManager.IsListening}, IsServer={networkManager.IsServer}, " +
			   $"IsClient={networkManager.IsClient}, IsConnectedClient={networkManager.IsConnectedClient}, " +
			   $"DisconnectReason='{networkManager.DisconnectReason}', " +
			   $"TransportDisconnectEvent={networkManager.NetworkConfig.NetworkTransport.DisconnectEvent}.";
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
		networkManager.SceneManager.OnLoadComplete += HandleWaitingRoomSceneLoadCompleted;
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

	private void HandleWaitingRoomSceneLoadCompleted(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
	{
		if (sceneName != _waitingRoomSceneName || clientId != NetworkManager.Singleton.LocalClientId) return;

		OnWaitingRoomSceneLoadComplete?.Invoke();
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

				// SpawnAsPlayerObject가 방금 스폰한 오브젝트를 ConnectedClients에 등록하므로 다시 읽는다.
				// 이걸 빼먹으면 playerObject가 계속 null이라 아래 TryGetComponent에서 터진다.
				playerObject = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;

				if (playerObject == null)
				{
					Debug.LogError($"[GameSessionManager] 클라이언트 {clientId}의 플레이어 오브젝트를 스폰하지 못했습니다.");
					continue;
				}
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
		if (localPlayerObject == null)
		{
			Debug.LogError($"[GameSessionManger] 초기화 중 localPlayerObject 발견하지 못함.");
			return;
		}

		if (localPlayerObject.TryGetComponent(out PlayerInteraction localPlayerInteraction))
		{
			localPlayerInteraction.InitializeOnGameScene();
		}

		if (localPlayerObject.TryGetComponent(out PlayerInventory localPlayerInventory))
		{
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
		// 로비 화면 복귀가 네트워크 왕복을 기다리지 않도록 완료를 기다리지 않는다.
		_ = ReleaseCurrentSessionAsync();
		SceneManager.LoadScene(_lobbySceneName);
	}

	// 내가 사유를 지정하고 나간 경우가 아니면 기존 기본 문구를 쓴다.
	private string ResolveLeaveReason()
	{
		if (!string.IsNullOrEmpty(_pendingLeaveReason)) return _pendingLeaveReason;

		// NGO는 서버가 사유를 보내지 않아도 영문 문자열을 채워두므로, 우리가 붙인 표식이 있을 때만 채택한다.
		string disconnectReason = NetworkManager.Singleton.DisconnectReason;
		if (!string.IsNullOrEmpty(disconnectReason) && disconnectReason.StartsWith(ServerReasonPrefix))
		{
			return disconnectReason.Substring(ServerReasonPrefix.Length);
		}

		if (_isLeavingVoluntarily) return "방을 나왔습니다";
		if (NetworkManager.Singleton.IsHost) return "연결이 끊겼습니다";

		// 내 연결이 끊겨 밀려난 경우와 호스트가 방을 닫은 경우는 원인이 달라 문구도 달라야 한다.
		return NetworkManager.Singleton.NetworkConfig.NetworkTransport.DisconnectEvent
			is NetworkTransport.DisconnectEvents.ProtocolTimeout
			or NetworkTransport.DisconnectEvents.ProtocolError
			or NetworkTransport.DisconnectEvents.MaxConnectionAttempts
			? "서버와의 연결이 끊어졌습니다"
			: "다른 플레이어의 접속이 끊어졌습니다";
	}

	// 사유를 지정하지 않으면 ResolveLeaveReason의 기본 문구("방을 나왔습니다")가 표시된다.
	public void LeaveSession() => LeaveSessionWithReason(null);

	// 로비에 표시할 사유를 지정해 퇴장한다.
	public void LeaveSessionWithReason(string reason)
	{
		_pendingLeaveReason = reason;
		_isLeavingVoluntarily = true;

		// LeaveAsync()의 로비 서비스 왕복을 먼저 기다리면 로비 복귀가 그만큼 늦어지고,
		// 그 사이에 서버의 킥 백스톱이 터진다. 연결부터 끊어 서버가 즉시 알게 하고,
		// 로비 멤버십 정리는 HandleClientDisconnected가 백그라운드로 이어서 처리한다.
		NetworkManager.Singleton.Shutdown();
	}

	// 방장이 대기방에서 "게임 시작"을 눌렀을 때 호출한다.
	public void StartGame()
	{
		if (!NetworkManager.Singleton.IsServer) return;

		NetworkManager.Singleton.SceneManager.LoadScene(_gameSceneName, LoadSceneMode.Single);
	}
}
