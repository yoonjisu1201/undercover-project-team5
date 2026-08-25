using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

/* 조인코드, 로비의 방 목록, 빠른 시작으로 입장 가능한 세션(방)을 만들고 참가하는 기능.
 * 방은 공개로 생성되어 로비 목록에 노출되며, 게임이 시작되면 잠겨서 목록에는 남되 입장만 막힌다.
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
	[Header("로컬 테스트")]
	[Tooltip("켜면 Relay 대신 직접 연결(127.0.0.1)로 방을 만든다. 같은 PC에서만 들어올 수 있다. "
		+ "Relay 장애로 방이 안 만들어질 때 테스트를 이어가려는 용도이므로, 배포 전에는 반드시 끈다.")]
	[SerializeField] private bool _useDirectNetworkForLocalTest;

	public ISession CurrentSession { get; private set; }
	public string JoinCode => CurrentSession?.Code;
	public string RoomName => CurrentSession?.Name;
	public string LastLeaveReason { get; set; }

	// 방 이름은 로비 목록에 그대로 노출되므로, 한 줄에 들어가는 길이로 제한한다.
	// TMP의 characterLimit은 글자 수를 세므로 한글도 10자까지 들어간다.
	public const int MaxRoomNameLength = 10;

	// 서버가 우리 코드에서 클라이언트를 내보낼 때 사유 앞에 붙이는 표식.
	// NGO가 자동으로 채우는 영문 사유("Client-1 disconnected by server." 등)와 구분하기 위함이다.
	public const string ServerReasonPrefix = "UC|";

	// 방 목록에서 빌드 버전을 대조하려면 세션 프로퍼티로 공개해야 한다.
	// 이 키가 없는 방은 버전 검사가 없던 구버전 빌드가 만든 방이다.
	private const string BuildVersionPropertyKey = "buildVersion";

	// 게임 중에는 방장이 나갔는지 다른 인원이 빠졌는지가 남은 사람 입장에서 다르지 않으므로 문구를 구분하지 않는다.
	private const string InGameHostLeftKey = "lobby_leave_peer_disconnected";

	// 대기방은 아직 게임이 시작되지 않아 방장 퇴장이 곧 방 해산이라, 그 사실을 그대로 알린다.
	private const string WaitingRoomHostLeftKey = "lobby_leave_host_left";

	// 현지화 테이블 이름. 서버가 보낸 키를 받는 쪽에서 풀 때 쓴다.
	private const string LocalizationTable = "Language Table";

	[Header("현지화 문구")]
	[Tooltip("방 이름을 비워두고 만들 때 붙는 기본 이름. {0} 에 번호가 들어간다.")]
	[SerializeField] private LocalizedString _defaultRoomName;

	[SerializeField] private LocalizedString _errorNetwork;
	[SerializeField] private LocalizedString _errorInvalidCode;
	[SerializeField] private LocalizedString _errorRoomGone;
	[SerializeField] private LocalizedString _errorRoomFull;
	[SerializeField] private LocalizedString _errorAlreadyJoined;
	[SerializeField] private LocalizedString _errorRateLimited;
	[SerializeField] private LocalizedString _errorRoomListFailed;

	[Tooltip("{0} 내 버전, {1} 방 버전")]
	[SerializeField] private LocalizedString _errorVersionMismatch;

	[SerializeField] private LocalizedString _versionUnknown;
	[SerializeField] private LocalizedString _leaveLeftRoom;
	[SerializeField] private LocalizedString _leaveDisconnected;
	[SerializeField] private LocalizedString _leaveServerDisconnected;
	[SerializeField] private LocalizedString _leavePeerDisconnected;

	public event Action<string> OnSessionCreated; // 조인코드 발급 완료
	public event Action OnSessionJoined;          // 조인코드로 참가 완료
	public event Action<string> OnSessionError;   // 실패 사유 전달
	public event Action OnSessionStarting;                            // 세션 생성/참가 시도 시작
	public event Action<AsyncOperation> OnWaitingRoomSceneLoadStarted; // 내 로컬 씬 로딩이 시작됨 (진행률 포함)
	public event Action OnWaitingRoomSceneLoadComplete; // 내 로컬 씬 로딩이 완료됨 (진행률 포함)

	private bool _isLeavingVoluntarily;
	private string _pendingLeaveReason;

	// 게임이 시작됐는지 서버가 즉시 판단하기 위한 플래그.
	// Lobby의 IsLocked는 반영에 네트워크 왕복이 필요해 접속 승인 시점에는 믿을 수 없다.
	private bool _isSessionLocked;

	// 퇴장 요청은 로비 복귀를 늦추지 않도록 기다리지 않는다. 다만 방금 나온 방이 목록에
	// 남지 않으려면 조회 전에는 끝나 있어야 하므로 참조를 들고 있는다.
	private Task _pendingLeaveTask;

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
	// 방 이름은 로비 목록에 노출된다. 비워두면 서로 구분되도록 임의의 번호를 붙인 기본 이름을 쓴다.
	public async void CreateSession(string roomName)
	{
		OnSessionStarting?.Invoke();
		string stage = "로그인 대기";
		try
		{
			await NetworkBootstrap.SignInTask; // 로그인 끝날 때까지 대기

			stage = "연결 승인 설정";
			PrepareConnectionApproval();

			// IsPrivate이면 목록 조회에 잡히지 않는다. 로비에 방을 노출하려면 공개로 만들어야 한다.
			var options = new SessionOptions
			{
				Name = ResolveRoomName(roomName),
				MaxPlayers = _maxPlayers,
				IsPrivate = false,
				// 목록 조회 결과에 실려야 하므로 Public으로 공개한다.
				SessionProperties = new Dictionary<string, SessionProperty>
				{
					[BuildVersionPropertyKey] = new(Application.version, VisibilityPropertyOptions.Public)
				}
			};

			// Relay 는 Unity 서버를 거쳐 연결한다. 그쪽이 죽으면(504 등) 방 생성 자체가 실패해서
			// 로컬 테스트도 못 한다. 직접 연결은 Relay 를 건너뛰지만 같은 PC 안에서만 통한다.
			options = _useDirectNetworkForLocalTest
				? options.WithDirectNetwork()
				: options.WithRelayNetwork();

			if (_useDirectNetworkForLocalTest)
			{
				Debug.LogWarning("[GameSessionManager] 직접 연결(로컬 전용)로 방을 만듭니다. 배포 전에 끄세요.", this);
			}

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

	// 입력이 비어 있을 때만 기본 이름을 만든다. 번호를 붙여 목록에서 서로 구분되게 한다.
	//
	// 방 이름은 세션에 저장되는 데이터라 보는 사람마다 다르게 보여줄 수 없다. 만드는 사람의
	// 언어로 한 번 정해지고 그대로 모두에게 노출된다. 그래도 여기서 현지화 문구를 쓰는 이유는,
	// 영어로 플레이하는 사람이 만든 방이 한글 이름을 갖는 것을 막기 위해서다.
	private string ResolveRoomName(string roomName)
		=> string.IsNullOrWhiteSpace(roomName)
			? _defaultRoomName.GetLocalizedString(UnityEngine.Random.Range(1000, 10000))
			: roomName.Trim();

	// 같은 실패라도 어떤 경로로 시도했는지에 따라 사용자에게 알려줄 원인이 다르다.
	private enum JoinRoute
	{
		Code, // 조인코드 입력
		List  // 로비 목록 선택 / 빠른 시작
	}

	// 클라이언트: 조인코드로 방에 참가한다.
	public void JoinSessionByCode(string joinCode)
		=> JoinSession(() => MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode), JoinRoute.Code);

	// 클라이언트: 로비 목록에서 고른 방에 조인코드 없이 참가한다.
	public void JoinSessionById(string sessionId)
		=> JoinSession(() => MultiplayerService.Instance.JoinSessionByIdAsync(sessionId), JoinRoute.List);

	// 참가 경로마다 요청 방식과 실패 문구만 다르고, 그 뒤 절차는 모두 같다.
	private async void JoinSession(Func<Task<ISession>> requestSession, JoinRoute route)
	{
		OnSessionStarting?.Invoke();
		string stage = "로그인 대기";
		try
		{
			await NetworkBootstrap.SignInTask; // 로그인 끝날 때까지 대기

			stage = "연결 승인 설정";
			PrepareConnectionApproval();

			stage = "세션 참가 요청";
			CurrentSession = await requestSession();

			stage = "빌드 버전 확인";
			// 구버전 빌드가 만든 방은 호스트에 버전 검사가 없어 그냥 승인해버린다.
			// 호스트를 믿을 수 없으므로 참가한 쪽에서도 확인하고 스스로 빠진다.
			string roomVersion = ReadRoomVersion(CurrentSession.Properties);
			if (roomVersion != Application.version)
			{
				await ReleaseCurrentSessionAsync();
				OnSessionError?.Invoke(_errorVersionMismatch.GetLocalizedString(
					Application.version, DescribeVersion(roomVersion)));
				return;
			}

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
			Debug.LogError($"[GameSessionManager] 세션 참가({route}) 중 '{stage}' 단계에서 오류가 발생했습니다.\n" +
						   $"오류 내용: [{DescribeError(e)}] {e.Message}");
			await ReleaseCurrentSessionAsync();
			OnSessionError?.Invoke(ToUserMessage(e, route));
		}
	}

	// 로비 목록에 뿌릴 공개 방 목록을 조회한다.
	// 정원이 찼거나 게임이 시작된 방도 그대로 돌려주고, 입장 가능 여부 판단은 호출부가 IsJoinable로 한다.
	// 조회에 실패하면 사유를 알린 뒤 null을 돌려준다. 빈 목록(방이 하나도 없음)과 구분해야 하기 때문이다.
	public async Task<IList<ISessionInfo>> QueryRoomsAsync()
	{
		try
		{
			await NetworkBootstrap.SignInTask; // 로그인 끝날 때까지 대기

			// 방금 나온 방의 삭제가 끝나기 전에 조회하면 사라진 방이 목록에 남는다.
			if (_pendingLeaveTask != null) await _pendingLeaveTask;

			var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions());
			return results.Sessions;
		}
		catch (Exception e)
		{
			Debug.LogError($"[GameSessionManager] 방 목록 조회 중 오류가 발생했습니다.\n" +
						   $"오류 내용: [{DescribeError(e)}] {e.Message}");
			OnSessionError?.Invoke(_errorRoomListFailed.GetLocalizedString());
			return null;
		}
	}

	// 빠른 시작: 입장 가능한 방 중 하나를 무작위로 골라 참가하고, 없으면 직접 방을 만든다.
	// 캐시된 목록을 쓰면 그사이 꽉 찬 방을 고르게 되므로 누른 시점에 다시 조회한다.
	public async void QuickJoinRandomRoom()
	{
		OnSessionStarting?.Invoke();

		var rooms = await QueryRoomsAsync();
		if (rooms == null) return; // 조회 실패 사유는 QueryRoomsAsync가 이미 알렸다

		List<ISessionInfo> joinableRooms = new();
		foreach (var room in rooms)
		{
			if (IsJoinable(room)) joinableRooms.Add(room);
		}

		// 안내만 하고 끝나면 유저가 방 만들기를 다시 눌러야 한다. 대신 호스트로 시작한다.
		// (조회 자체가 실패한 경우는 위에서 이미 돌아갔으므로, 여기는 "방이 하나도 없음"이 확실하다)
		if (joinableRooms.Count == 0)
		{
			CreateSession(null); // 이름을 비우면 번호를 붙인 기본 이름이 쓰인다
			return;
		}

		JoinSessionById(joinableRooms[UnityEngine.Random.Range(0, joinableRooms.Count)].Id);
	}

	// 정원이 찼거나(AvailableSlots) 게임이 시작되어 잠긴(IsLocked) 방에는 들어갈 수 없다.
	// 목록의 회색 처리와 빠른 시작이 같은 기준을 쓰도록 한곳에서 판단한다.
	public static bool IsJoinable(ISessionInfo room)
		=> room.AvailableSlots > 0 && !room.IsLocked && IsVersionMatched(room);

	// 세션 프로퍼티에 실린 방의 빌드 버전. 프로퍼티가 없으면 빈 문자열이 된다.
	public static string ReadRoomVersion(IReadOnlyDictionary<string, SessionProperty> properties)
		=> properties != null && properties.TryGetValue(BuildVersionPropertyKey, out var property)
			? property.Value
			: string.Empty;

	public static bool IsVersionMatched(ISessionInfo room)
		=> ReadRoomVersion(room.Properties) == Application.version;

	// Unity Lobby 멤버십은 Netcode 연결과 별개라, 연결이 끊겨도 로비에는 멤버로 남는다.
	// 명시적으로 나가지 않으면 같은 방 코드로 재참가할 때 SessionConflict
	// ("player is already a member of the lobby")가 난다.
	private async Task ReleaseCurrentSessionAsync()
	{
		// 음성 채널은 Netcode 연결과 별개라, 연결이 서지 않은 채 실패해도 저절로 빠지지 않는다.
		// 남겨두면 입장하지 못한 클라이언트가 그 방의 대화를 계속 듣게 된다.
		VivoxManager.Instance.LeaveSessionChannel();

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
	private string ToUserMessage(Exception e, JoinRoute? route = null)
	{
		string networkMessage = _errorNetwork.GetLocalizedString();
		string invalidCodeMessage = _errorInvalidCode.GetLocalizedString();
		string roomGoneMessage = _errorRoomGone.GetLocalizedString();

		// 로그인이나 서비스 초기화 실패는 SessionException이 아니다. 방 코드와 무관한 실패다.
		if (e is not SessionException sessionException) return networkMessage;

		// 서버가 승인을 거부하면 SDK는 참가 요청 자체를 NetworkManager 시작 실패로 던진다.
		// 이때는 서버가 붙인 사유가 원인을 정확히 알려주므로 추측성 문구로 덮지 않는다.
		// (이 두 에러는 NGO가 시작된 뒤에만 나오고, 시작 시 DisconnectReason이 비워져 값이 최신임이 보장된다)
		if (sessionException.Error is SessionError.NetworkManagerStartFailed or SessionError.NetworkSetupFailed)
		{
			string serverReason = ReadServerReason();
			if (serverReason != null) return serverReason;
		}

		// 서비스는 코드 형식 위반과 네트워크 오류를 둘 다 Unknown으로만 알려줘 구분할 수 없다.
		// 인터넷이 아예 끊긴 상태라면 코드 문제가 아니라고 확실히 말할 수 있다.
		bool isOffline = Application.internetReachability == NetworkReachability.NotReachable;
		string defaultMessage = route == JoinRoute.Code && !isOffline ? invalidCodeMessage : networkMessage;

		// 방을 찾지 못한 원인은 경로마다 다르다. 목록 선택은 그사이 방이 닫힌 경우가 흔하다.
		string sessionNotFoundMessage = route == JoinRoute.List ? roomGoneMessage : invalidCodeMessage;

		switch (sessionException.Error)
		{
			case SessionError.SessionNotFound:
			case SessionError.SessionDeleted:
			case SessionError.NetworkManagerStartFailed:
			case SessionError.NetworkSetupFailed:
				return sessionNotFoundMessage;
			case SessionError.SessionConflict:
				return _errorAlreadyJoined.GetLocalizedString();
			case SessionError.RateLimitExceeded:
				return _errorRateLimited.GetLocalizedString();
			default:
				// 정원 초과와 잠긴 방은 별도 SessionError 없이 Unknown으로 넘어와 메시지로만 구분할 수 있다.
				// (각각 "lobby is full", "lobby is locked"로 온다)
				if (sessionException.Message.Contains("full", StringComparison.OrdinalIgnoreCase))
				{
					return _errorRoomFull.GetLocalizedString();
				}

				if (sessionException.Message.Contains("locked", StringComparison.OrdinalIgnoreCase))
				{
					return roomGoneMessage;
				}

				return defaultMessage;
		}
	}

	// 로비 씬에는 바닥이 없어 접속 즉시 자동 스폰되면 캐릭터가 떨어진다.
	// 접속 승인 방식은 NetworkManager가 시작(StartHost/StartClient)되기 전에 설정해야 하므로
	// 세션 생성/참가보다 먼저 호출한다.
	private void PrepareConnectionApproval()
	{
		// 싱글턴이 씬을 넘어 살아남으므로, 게임을 시작했던 상태가 다음 방까지 따라오면 아무도 못 들어온다.
		_isSessionLocked = false;

		var networkManager = NetworkManager.Singleton;
		networkManager.NetworkConfig.ConnectionApproval = true;
		// 승인 단계에서 빌드 버전을 대조하려면 클라이언트가 자기 버전을 미리 실어 보내야 한다.
		networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(Application.version);
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

	// 서버가 클라이언트에게 보낼 사유. 문장이 아니라 현지화 키를 싣는다.
	//
	// 문장을 그대로 보내면 서버(방장) 언어로 굳어져서, 다른 언어를 쓰는 참가자에게도 그 언어로 뜬다.
	// 키만 보내고 받는 쪽에서 풀면 각자 자기 언어로 본다. 인자가 있으면 '|' 로 이어 붙인다.
	public static string ServerReason(string localizationKey, params string[] arguments)
		=> arguments == null || arguments.Length == 0
			? ServerReasonPrefix + localizationKey
			: ServerReasonPrefix + localizationKey + "|" + string.Join("|", arguments);

	// 우리 서버가 붙인 사유를 받는 쪽 언어로 풀어서 돌려준다. 없으면 null.
	// NGO는 서버가 사유를 보내지 않아도 영문 문자열을 채워두므로, 표식이 있을 때만 채택한다.
	private static string ReadServerReason()
	{
		var networkManager = NetworkManager.Singleton;
		if (networkManager == null) return null;

		string reason = networkManager.DisconnectReason;
		if (string.IsNullOrEmpty(reason) || !reason.StartsWith(ServerReasonPrefix))
		{
			return null;
		}

		string[] parts = reason.Substring(ServerReasonPrefix.Length).Split('|');
		string[] arguments = new string[parts.Length - 1];
		for (int i = 1; i < parts.Length; i++) arguments[i - 1] = parts[i];

		// 키가 테이블에 없으면 빈 문자열이 온다. 그때는 사유가 없는 것으로 취급해 기본 문구가 뜨게 한다.
		string text = Localize(parts[0], arguments);
		return string.IsNullOrEmpty(text) ? null : text;
	}

	// 현지화 키를 지금 언어의 문장으로 바꾼다. 서버가 보낸 키와 로컬에서 만든 키 모두 이 경로를 쓴다.
	private static string Localize(string localizationKey, params string[] arguments)
	{
		if (string.IsNullOrEmpty(localizationKey)) return null;

		var localized = new LocalizedString(LocalizationTable, localizationKey);
		if (arguments != null && arguments.Length > 0)
		{
			object[] boxed = new object[arguments.Length];
			for (int i = 0; i < arguments.Length; i++) boxed[i] = arguments[i];
			localized.Arguments = boxed;
		}

		return localized.GetLocalizedString();
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
		// 게임이 시작된 뒤 붙은 클라이언트는 대기방 씬에서만 하는 스폰을 받지 못해 준비 보고를
		// 영원히 못 한다. 붙이고 나서 정리하는 대신 승인 단계에서 막는다.
		if (_isSessionLocked)
		{
			response.Approved = false;
			response.Reason = ServerReason("lobby_error_room_started");
			return;
		}

		// 빌드가 다르면 프리팹 목록은 같아도 수치·판정이 어긋나 원인 불명의 오동작이 난다.
		// 버전을 싣지 않는 구버전 빌드는 빈 값이 되어 자연히 불일치로 걸러진다.
		string clientVersion = ReadClientVersion(request.Payload);
		if (clientVersion != Application.version)
		{
			response.Approved = false;
			response.Reason = ServerReason(
				"lobby_error_version_mismatch", DescribeVersion(clientVersion), Application.version);
			return;
		}

		response.Approved = true;
		response.CreatePlayerObject = false;
	}

	// 승인 요청에 실린 접속자의 빌드 버전. 버전을 싣지 않는 빌드는 빈 문자열이 된다.
	private static string ReadClientVersion(byte[] payload)
		=> payload == null || payload.Length == 0 ? string.Empty : Encoding.UTF8.GetString(payload);

	private string DescribeVersion(string version)
		=> string.IsNullOrEmpty(version) ? _versionUnknown.GetLocalizedString() : version;

	private void HandleWaitingRoomSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
	{
		if (sceneName != _waitingRoomSceneName) return;

		// 플레이어 오브젝트는 씬 전환에도 유지되어 대기방 복귀 시 OnNetworkSpawn이 다시 호출되지 않는다.
		// 따라서 서버 여부와 관계없이 각 클라이언트가 자신의 로컬 PlayerInteraction을 다시 초기화해
		// 새 대기방의 상호작용 UI 참조를 연결한다.
		var localPlayerObject = NetworkManager.Singleton.LocalClient?.PlayerObject;

		if (localPlayerObject != null && localPlayerObject.TryGetComponent(out PlayerInteraction localPlayerInteraction))
		{
			localPlayerInteraction.InitializeOnGameScene();
		}

		// 위의 UI 재연결은 각 클라이언트의 로컬 작업이고, 아래 세션 해제와 플레이어 초기화는 서버 권한 작업이다.
		if (!NetworkManager.Singleton.IsServer) return;

		// 라운드가 끝나 대기방으로 돌아왔으면 다시 입장을 받아야 한다.
		// 방 생성 직후의 첫 진입에서도 호출되지만, 이미 풀려 있으면 서버 요청 없이 그냥 반환된다.
		SetSessionLocked(false);

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
				// 새 대기방 레이아웃의 명시적 입장 위치로 복귀시키되, 지점 누락 시 전체 복귀가 중단되지 않게 원점을 사용한다.
                GameObject WaitingRoomSpawnPointObj = GameObject.Find("WaitingRoomSpawnPoint");

                Vector3 WaitingroomSpawnPoint = WaitingRoomSpawnPointObj != null
					? WaitingRoomSpawnPointObj.transform.position
					: Vector3.zero;

				player.TeleportToPosition(WaitingroomSpawnPoint, playerObject.transform.rotation);
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

		var playerInstance = InstantiatePlayerAtWaitingRoomSpawn(NetworkManager.Singleton.NetworkConfig.PlayerPrefab);
		playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
	}

	private static GameObject InstantiatePlayerAtWaitingRoomSpawn(GameObject playerPrefab)
	{
		// 최초 입장도 씬에 배치한 위치와 방향을 사용해 원점이나 구조물 내부에 생성되지 않게 한다.
		Transform spawnPoint = GameObject.Find("WaitingRoomSpawnPoint").transform;
		return Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
	}

	// 내 연결이 끊긴 경우에만 로비로 돌아간다 (자진 퇴장/호스트가 나가서 강제로 끊긴 경우 모두 포함).
	private void HandleClientDisconnected(ulong clientId)
	{
		if (clientId != NetworkManager.Singleton.LocalClientId) return;

		LastLeaveReason = ResolveLeaveReason();
		_pendingLeaveReason = null;
		_isLeavingVoluntarily = false;

		// 로비 화면 복귀가 네트워크 왕복을 기다리지 않도록 완료를 기다리지 않는다.
		_pendingLeaveTask = ReleaseCurrentSessionAsync();
		SceneManager.LoadScene(_lobbySceneName);
	}

	// 내가 사유를 지정하고 나간 경우가 아니면 기존 기본 문구를 쓴다.
	private string ResolveLeaveReason()
	{
		if (!string.IsNullOrEmpty(_pendingLeaveReason)) return _pendingLeaveReason;

		string serverReason = ReadServerReason();
		if (serverReason != null) return serverReason;

		if (_isLeavingVoluntarily) return _leaveLeftRoom.GetLocalizedString();
		if (NetworkManager.Singleton.IsHost) return _leaveDisconnected.GetLocalizedString();

		// 내 연결이 끊겨 밀려난 경우와 호스트가 방을 닫은 경우는 원인이 달라 문구도 달라야 한다.
		return NetworkManager.Singleton.NetworkConfig.NetworkTransport.DisconnectEvent
			is NetworkTransport.DisconnectEvents.ProtocolTimeout
			or NetworkTransport.DisconnectEvents.ProtocolError
			or NetworkTransport.DisconnectEvents.MaxConnectionAttempts
			? _leaveServerDisconnected.GetLocalizedString()
			: _leavePeerDisconnected.GetLocalizedString();
	}

	// 사유를 지정하지 않으면 ResolveLeaveReason의 기본 문구가 표시된다.
	public void LeaveSession() => LeaveSessionWithReason(null);

	// 로비에 표시할 사유를 현지화 키로 지정해 퇴장한다.
	//
	// 나가는 본인은 지금 언어로 풀어서 들고 가고, 남는 사람들에게는 키를 그대로 보낸다.
	// 문장을 보내면 방장 언어로 굳어져 다른 언어 참가자에게도 그 언어로 뜬다.
	public void LeaveSessionWithReason(string localizationKey, params string[] arguments)
	{
		_pendingLeaveReason = Localize(localizationKey, arguments);
		_isLeavingVoluntarily = true;

		// 호스트가 그냥 Shutdown하면 끊김 통보가 전달되지 못한 클라이언트는 전송 계층
		// 타임아웃이 다 돌 때까지 방에 남아 있게 된다. 나가기 전에 사유를 붙여 직접 내보낸다.
		if (NetworkManager.Singleton.IsServer)
		{
			DisconnectRemoteClients(string.IsNullOrEmpty(localizationKey)
				? DefaultRemainingClientsReason()
				: ServerReason(localizationKey, arguments));
		}

		// LeaveAsync()의 로비 서비스 왕복을 먼저 기다리면 로비 복귀가 그만큼 늦어지고,
		// 그 사이에 서버의 킥 백스톱이 터진다. 연결부터 끊어 서버가 즉시 알게 하고,
		// 로비 멤버십 정리는 HandleClientDisconnected가 백그라운드로 이어서 처리한다.
		NetworkManager.Singleton.Shutdown();
	}

	// 방장이 사유를 지정하지 않고 나갈 때, 남은 인원에게 보낼 문구를 상황에 맞게 고른다.
	private string DefaultRemainingClientsReason()
		=> ServerReason(SceneManager.GetActiveScene().name == _waitingRoomSceneName
			? WaitingRoomHostLeftKey
			: InGameHostLeftKey);

	// DisconnectClient가 순회 중인 목록을 바꾸므로, 대상을 먼저 모아두고 나서 내보낸다.
	// reason 은 ServerReason() 으로 만든 값이어야 한다(표식 + 현지화 키).
	private static void DisconnectRemoteClients(string reason)
	{
		var networkManager = NetworkManager.Singleton;

		List<ulong> clientsToDisconnect = new();
		foreach (ulong clientId in networkManager.ConnectedClientsIds)
		{
			if (clientId != networkManager.LocalClientId)
			{
				clientsToDisconnect.Add(clientId);
			}
		}

		foreach (ulong clientId in clientsToDisconnect)
		{
			networkManager.DisconnectClient(clientId, reason);
		}
	}

	// 방장이 대기방에서 "게임 시작"을 눌렀을 때 호출한다.
	public void StartGame()
	{
		if (!NetworkManager.Singleton.IsServer) return;

		SetSessionLocked(true);

		NetworkManager.Singleton.SceneManager.LoadScene(_gameSceneName, LoadSceneMode.Single);
	}

	// 게임이 시작된 방은 로비 목록에 계속 보이되 입장만 막혀야 한다.
	// 세션을 삭제하지 않고 잠그면 목록 조회에는 그대로 나오면서 IsLocked로 구분된다.
	// 잠금 성패가 게임 진행을 막을 이유는 없으므로 결과를 기다리지 않고 로그만 남긴다.
	private async void SetSessionLocked(bool isLocked)
	{
		// 서버 판정용 플래그를 먼저 세운다. Lobby 반영을 기다리는 사이에 들어오는 접속도 막아야 한다.
		_isSessionLocked = isLocked;

		try
		{
			var hostSession = CurrentSession?.AsHost();
			if (hostSession == null) return;

			hostSession.IsLocked = isLocked;
			await hostSession.SavePropertiesAsync();
		}
		catch (Exception e)
		{
			Debug.LogWarning($"[GameSessionManager] 세션 잠금 상태 변경에 실패했습니다: {e.Message}");
		}
	}
}
