using TMPro;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

// 로비 방 목록의 한 줄. 방 이름과 인원을 보여주고, 들어갈 수 없는 방은 회색으로 표시한다.

public class RoomListEntryUI : MonoBehaviour
{
	[Header("참조")]
	[SerializeField] private TextMeshProUGUI _roomNameText;
	[SerializeField] private TextMeshProUGUI _playerCountText;
	[SerializeField] private TextMeshProUGUI _statusText;
	[SerializeField] private GameObject _lockIcon;
	// 클릭 처리는 인스펙터의 OnClick()에 연결한다. 이 참조는 입장 불가 방을 비활성화하는 데만 쓴다.
	[SerializeField] private Button _joinButton;

	[Header("현지화 문구")]
	[SerializeField] private LocalizedString _statusInGame;
	[SerializeField] private LocalizedString _statusFull;
	[SerializeField] private LocalizedString _statusVersionUnknown;

	[Tooltip("방 버전 표시. {0} 에 버전 문자열이 들어간다.")]
	[SerializeField] private LocalizedString _statusVersion;

	[Header("글자 색")]
	[SerializeField] private Color _joinableColor = Color.white;
	[SerializeField] private Color _unjoinableColor = new(0.5f, 0.5f, 0.5f, 1f);
	[SerializeField] private Color _inGameColor = new(1f, 0.65f, 0.2f, 1f);
	[SerializeField] private Color _fullColor = new(1f, 0.25f, 0.25f, 1f);
	[SerializeField] private Color _versionMismatchColor = new(0.55f, 0.55f, 0.9f, 1f);

	private string _sessionId;

	public void Bind(ISessionInfo room)
	{
		_sessionId = room.Id;
		_roomNameText.text = room.Name;
		_playerCountText.text = $"{room.MaxPlayers - room.AvailableSlots}/{room.MaxPlayers}";

		// 정원이 찬 방과 이미 시작된 방도 방이 있다는 건 보여야 하므로, 지우지 않고 회색으로만 표시한다.
		bool isJoinable = GameSessionManager.IsJoinable(room);
		_joinButton.interactable = isJoinable;
		_lockIcon.SetActive(!isJoinable);

		if (room.IsLocked)
		{
			_statusText.text = _statusInGame.GetLocalizedString();
			_statusText.color = _inGameColor;
		}
		else if (room.AvailableSlots <= 0)
		{
			_statusText.text = _statusFull.GetLocalizedString();
			_statusText.color = _fullColor;
		}
		else if (!GameSessionManager.IsVersionMatched(room))
		{
			string roomVersion = GameSessionManager.ReadRoomVersion(room.Properties);
			_statusText.text = string.IsNullOrEmpty(roomVersion)
				? _statusVersionUnknown.GetLocalizedString()
				: _statusVersion.GetLocalizedString(roomVersion);
			_statusText.color = _versionMismatchColor;
		}
		else
		{
			_statusText.text = string.Empty;
		}

		Color textColor = isJoinable ? _joinableColor : _unjoinableColor;
		_roomNameText.color = textColor;
		_playerCountText.color = textColor;
	}

	// 인스펙터의 Button > OnClick()에 연결한다.
	public void JoinRoom()
	{
		GameSessionManager.Instance.JoinSessionById(_sessionId);
	}
}
