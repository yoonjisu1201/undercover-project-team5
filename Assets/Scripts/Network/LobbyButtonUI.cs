using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 로비 화면의 UI(방 생성, 방 목록, 조인코드 입력, 빠른 시작)와 GameSessionManager를 연결한다.

public class LobbyButtonUI : MonoBehaviour
{
	[Header("참조")]
	[SerializeField] private Button _createButton;
	[SerializeField] private Button _joinButton;
	[SerializeField] private TMP_InputField _joinCodeInputField;
	[SerializeField] private TextMeshProUGUI _leaveReasonText;
	[SerializeField] private TextMeshProUGUI _buildVersionText;

	[Header("방 만들기")]
	[SerializeField] private TMP_InputField _roomNameInputField;

	[Header("방 목록")]
	[SerializeField] private Button _refreshButton;
	[SerializeField] private RoomListEntryUI _roomEntryPrefab;
	[SerializeField] private Transform _roomListContent;

	// 목록 조회는 서비스에서 초당 1회로 제한되어 있어, 새로고침 연타가 그대로 조회 실패로 이어진다.
	private const int RefreshCooldownMilliseconds = 2000;

	private bool _isRefreshing;

	private void Start()
	{
		_createButton.onClick.AddListener(HandleCreateButtonClicked);
		_joinButton.onClick.AddListener(HandleJoinButtonClicked);

		_roomNameInputField.characterLimit = GameSessionManager.MaxRoomNameLength;

		// 다른 버전끼리는 방에 참가할 수 없으므로, 참가를 시도하기 전에 서로 버전을 맞춰볼 수 있게 노출한다.
		_buildVersionText.text = $"v{Application.version}";

		GameSessionManager.Instance.OnSessionCreated += HandleSessionCreated;
		GameSessionManager.Instance.OnSessionJoined += HandleSessionJoined;
		GameSessionManager.Instance.OnSessionError += HandleSessionError;

		ShowLeaveReasonIfAny();

		// 방에서 나와 로비로 돌아올 때도 이 씬이 새로 로드되므로, 여기서 한 번만 조회하면 된다.
		RefreshRoomList();
	}

	// 인스펙터의 새로고침 버튼 OnClick()과 빠른 시작 버튼 OnClick()에 연결한다.
	public void RefreshRoomList() => RefreshRoomListAsync().Forget();

	public void QuickStart() => GameSessionManager.Instance.QuickJoinRandomRoom();

	// 주기적으로 자동 갱신하지 않는 이유: 목록이 저절로 재정렬되면 누르려던 방 대신 다른 방에 들어가게 되고,
	// 조회 자체도 초당 1회 제한이 있어 폴링이 실패로 이어진다.
	private async UniTaskVoid RefreshRoomListAsync()
	{
		if (_isRefreshing) return;

		_isRefreshing = true;
		_refreshButton.interactable = false; // 눌리지 않는 이유가 보이도록 비활성 상태로 둔다

		IList<ISessionInfo> rooms = await GameSessionManager.Instance.QueryRoomsAsync();

		// 조회를 기다리는 동안 방에 입장해 씬이 바뀌었으면 이미 파괴된 UI를 건드리게 된다.
		if (this == null) return;

		// 조회 실패(null)면 화면의 기존 목록을 지우지 않고 그대로 둔다. 사유는 이미 안내되었다.
		if (rooms != null) PopulateRoomList(rooms);

		// 연타가 조회 제한에 걸리지 않도록, 조회가 끝난 시점부터 쿨다운을 센다.
		bool canceled = await UniTask
			.Delay(RefreshCooldownMilliseconds, cancellationToken: this.GetCancellationTokenOnDestroy())
			.SuppressCancellationThrow();
		if (canceled) return;

		_isRefreshing = false;
		_refreshButton.interactable = true;
	}

	// 목록은 매번 통째로 다시 만든다. 방 개수가 많지 않아 재사용 풀을 둘 이유가 없다.
	private void PopulateRoomList(IList<ISessionInfo> rooms)
	{
		foreach (Transform entry in _roomListContent)
		{
			Destroy(entry.gameObject);
		}

		foreach (var room in rooms)
		{
			Instantiate(_roomEntryPrefab, _roomListContent).Bind(room);
		}
	}

	// 방에서 로비로 돌아온 경우에만(자진 퇴장/호스트 퇴장/연결 끊김) 사유를 잠깐 보여준다.
	private void ShowLeaveReasonIfAny()
	{
		var reason = GameSessionManager.Instance.LastLeaveReason;
		if (string.IsNullOrEmpty(reason)) return;

		GameSessionManager.Instance.LastLeaveReason = null;
		ShowReasonText(reason);
	}

	// 퇴장 사유와 방 입장 실패 사유를 같은 자리에 같은 방식으로 띄운다.
	private void ShowReasonText(string reason)
	{
		CancelInvoke(nameof(HideLeaveReasonText)); // 연속 실패 시 앞선 숨김 예약이 새 문구를 지우지 않도록
		_leaveReasonText.text = reason;
		_leaveReasonText.gameObject.SetActive(true);
		Invoke(nameof(HideLeaveReasonText), 2f);
	}

	private void HideLeaveReasonText()
	{
		_leaveReasonText.gameObject.SetActive(false);
	}

	private void OnDestroy()
	{
		_createButton.onClick.RemoveListener(HandleCreateButtonClicked);
		_joinButton.onClick.RemoveListener(HandleJoinButtonClicked);

		GameSessionManager.Instance.OnSessionCreated -= HandleSessionCreated;
		GameSessionManager.Instance.OnSessionJoined -= HandleSessionJoined;
		GameSessionManager.Instance.OnSessionError -= HandleSessionError;
	}

	private void HandleCreateButtonClicked()
	{
		// 비워두면 GameSessionManager가 번호를 붙인 기본 이름을 대신 만든다.
		GameSessionManager.Instance.CreateSession(_roomNameInputField.text);
	}

	private void HandleJoinButtonClicked()
	{
		// 빈 값으로 요청하면 서버까지 갔다 와서 애매한 오류가 뜨므로 여기서 먼저 막는다.
		if (string.IsNullOrWhiteSpace(_joinCodeInputField.text))
		{
			ShowReasonText("방 코드를 입력해주세요");
			return;
		}

		GameSessionManager.Instance.JoinSessionByCode(_joinCodeInputField.text);
	}

	private void HandleSessionCreated(string joinCode)
	{
		ReleaseInputFocus();
		Debug.Log($"세션 생성 완료, 조인코드: {joinCode}");
	}

	private void HandleSessionJoined()
	{
		ReleaseInputFocus();
		Debug.Log("세션 참가 완료");
	}

	// 인풋필드가 포커스를 유지하면 WASD 같은 이동 입력이 여기로 들어가버리므로, 세션 성공 시 포커스를 해제한다.
	private void ReleaseInputFocus()
	{
		_joinCodeInputField.DeactivateInputField();
		_roomNameInputField.DeactivateInputField();
		EventSystem.current.SetSelectedGameObject(null);
	}

	private void HandleSessionError(string message)
	{
		Debug.LogError($"세션 오류: {message}");
		ShowReasonText(message);
	}
}
