using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

/* 조인코드로만 입장 가능한 세션(방)을 만들고 참가하는 기능.
 * 랜덤 매칭, 빠른 시작(QuickJoin), 공개 세션 목록 조회는 사용하지 않는다.
 */
public class GameSessionManager : MonoBehaviour
{
	public static GameSessionManager Instance { get; private set; }

	[Header("세션 설정")]
	[SerializeField] private int _maxPlayers = 4;
	[SerializeField] private string _gameplaySceneName = "TestRoom1";
	[SerializeField] private string _lobbySceneName = "Lobby";

	public ISession CurrentSession { get; private set; }
	public string JoinCode => CurrentSession?.Code;
	public string LastLeaveReason { get; set; }

	public event Action<string> OnSessionCreated; // 조인코드 발급 완료
	public event Action OnSessionJoined;          // 조인코드로 참가 완료
	public event Action<string> OnSessionError;   // 실패 사유 전달

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

		NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleGameplaySceneLoaded;
		NetworkManager.Singleton.SceneManager.OnSynchronizeComplete -= HandleClientSynchronized;
		NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
	}

	// 호스트: 방을 만들고 조인코드를 발급받는다.
	public async void CreateSession()
	{
		try
		{
			await NetworkBootstrap.SignInTask; // 로그인 끝날 때까지 대기

			PrepareConnectionApproval();

			var options = new SessionOptions
			{
				MaxPlayers = _maxPlayers,
				IsPrivate = true
			}.WithRelayNetwork();

			CurrentSession = await MultiplayerService.Instance.CreateSessionAsync(options);
			VivoxManager.Instance.JoinSessionChannel(CurrentSession.Code);
			SubscribeSceneEvents();
			OnSessionCreated?.Invoke(CurrentSession.Code);

			if (NetworkManager.Singleton.IsServer)
			{
				NetworkManager.Singleton.SceneManager.LoadScene(_gameplaySceneName, LoadSceneMode.Single);
			}
		}
		catch (Exception e)
		{
			OnSessionError?.Invoke(e.Message);
		}
	}

	// 클라이언트: 조인코드로 방에 참가한다.
	public async void JoinSessionByCode(string joinCode)
	{
		try
		{
			await NetworkBootstrap.SignInTask; // 로그인 끝날 때까지 대기

			PrepareConnectionApproval();

			CurrentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode);
            VivoxManager.Instance.JoinSessionChannel(CurrentSession.Code);
            SubscribeSceneEvents();
			OnSessionJoined?.Invoke();
		}
		catch (Exception e)
		{
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
	// 게임플레이 씬 로드/동기화가 완료되면 서버가 직접 스폰한다.
	private void SubscribeSceneEvents()
	{
		var networkManager = NetworkManager.Singleton;

		networkManager.SceneManager.OnLoadEventCompleted -= HandleGameplaySceneLoaded;
		networkManager.SceneManager.OnLoadEventCompleted += HandleGameplaySceneLoaded;

		// 씬 전환이 끝난 뒤 조인코드로 참가하는 클라이언트는 OnLoadEventCompleted가 아니라
		// 이쪽(최초 동기화 완료)으로 들어온다.
		networkManager.SceneManager.OnSynchronizeComplete -= HandleClientSynchronized;
		networkManager.SceneManager.OnSynchronizeComplete += HandleClientSynchronized;

		// 호스트가 나가서 강제로 끊기든, 내가 직접 나가기를 누르든 동일하게 로비로 돌아간다.
		networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
		networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
	}

	private void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
	{
		response.Approved = true;
		response.CreatePlayerObject = false;
	}

	private void HandleGameplaySceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
	{
		if (sceneName != _gameplaySceneName || !NetworkManager.Singleton.IsServer) return;

		foreach (var clientId in clientsCompleted)
		{
			SpawnPlayerForClient(clientId);
		}
	}

	private void HandleClientSynchronized(ulong clientId)
	{
		if (!NetworkManager.Singleton.IsServer) return;
		if (SceneManager.GetActiveScene().name != _gameplaySceneName) return;

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
}
