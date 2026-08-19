using TMPro;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

// 로비 방 목록의 한 줄. 방 이름과 인원을 보여주고, 들어갈 수 없는 방은 회색으로 표시한다.

public class RoomListEntryUI : MonoBehaviour
{
	[Header("참조")]
	[SerializeField] private TextMeshProUGUI _roomNameText;
	[SerializeField] private TextMeshProUGUI _playerCountText;
	// 클릭 처리는 인스펙터의 OnClick()에 연결한다. 이 참조는 입장 불가 방을 비활성화하는 데만 쓴다.
	[SerializeField] private Button _joinButton;

	[Header("글자 색")]
	[SerializeField] private Color _joinableColor = Color.white;
	[SerializeField] private Color _unjoinableColor = new(0.5f, 0.5f, 0.5f, 1f);

	private string _sessionId;

	public void Bind(ISessionInfo room)
	{
		_sessionId = room.Id;
		_roomNameText.text = room.Name;
		_playerCountText.text = $"{room.MaxPlayers - room.AvailableSlots}/{room.MaxPlayers}";

		// 정원이 찬 방과 이미 시작된 방도 방이 있다는 건 보여야 하므로, 지우지 않고 회색으로만 표시한다.
		bool isJoinable = GameSessionManager.IsJoinable(room);
		_joinButton.interactable = isJoinable;

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
