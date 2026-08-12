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
			else
			{
                if (playerObject.TryGetComponent(out PlayerHealth playerHealth) &&
					playerObject.TryGetComponent(out PlayerMoveSample player))
                {
                    playerHealth.ResetForNewRound();
                    player.TeleportToPosition(Vector3.zero, playerObject.transform.rotation);
                }
            }
		}
	}

	// 플레이어 오브젝트는 대기방(WaitingRoom_T)에서 스폰된 채로 게임씬(GameScene_T) 전환에도
	// 파괴되지 않고 그대로 유지된다. 그래서 PlayerInteraction.OnNetworkSpawn()은 대기방에 있을 때
	// 딱 한 번만 실행되고, 그 시점엔 게임씬에만 있는 InventoryUI(InventoryCanvas)를 찾을 수 없어
	// _inventoryUI가 null로 남는다. 게임씬 로드가 끝난 지금 시점에 다시 호출해주면
	// InventoryUI를 정상적으로 찾아 인벤토리를 바인딩할 수 있다.
	// (PlayerInteraction/InventoryUI 스크립트는 건드리지 않고 이 씬 전환 스크립트에서만 처리한다.)
	private void HandleGameSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
	{
		if (sceneName != _gameSceneName) return;

		// 같은 플레이어라도 머신마다 별도의 PlayerInteraction 복제본을 가진다.
		// 서버(호스트)에 있는 복제본 - RequestDropRpc 같은 [Rpc(SendTo.Server)] 로직이 참조하는,
		// "서버 판정용" 복제본 - 과 각 클라이언트 자기 컴퓨터에 있는,
		// 자기 화면의 인벤토리 UI를 그리는 데 쓰이는 "각자 화면 표시용" 복제본은 서로 다른 인스턴스라서
		// 아래 두 처리를 각각 따로 해줘야 한다. (호스트는 이 둘이 같은 인스턴스라 우연히 같이 고쳐졌던 것)

		// 1) 서버 판정용 복제본 처리: 서버에서만 실행. 접속한 모든 플레이어의 "서버에 있는 복제본"을
		//    다시 스폰시켜서 _itemCatalog를 다시 찾게 한다. 이게 안 되어 있으면 호스트가 아닌 다른
		//    클라이언트가 드롭 등 서버 판정이 필요한 상호작용을 할 때 서버측에서 조용히 막힌다.
		if (NetworkManager.Singleton.IsServer)
		{
			foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
			{
				if (client.PlayerObject != null && client.PlayerObject.TryGetComponent(out PlayerInteraction serverSidePlayerInteraction))
				{
					serverSidePlayerInteraction.OnNetworkSpawn();
				}
			}
		}

		// 3) 각자 화면 표시용 복제본 처리: 이 코드는 호스트/클라이언트 각자의 컴퓨터에서 개별적으로
		//    실행되므로, LocalClient.PlayerObject는 항상 "지금 이 코드를 실행 중인 컴퓨터 자신의
		//    캐릭터"를 가리킨다. 그 복제본을 다시 스폰시켜서 InventoryUI(아이콘/프롬프트 텍스트)를
		//    다시 바인딩한다.
		var localPlayerObject = NetworkManager.Singleton.LocalClient?.PlayerObject;
		if (localPlayerObject != null && localPlayerObject.TryGetComponent(out PlayerInteraction localPlayerInteraction))
		{
			localPlayerInteraction.OnNetworkSpawn();
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
