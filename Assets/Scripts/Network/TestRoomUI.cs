using TMPro;
using UnityEngine;
using UnityEngine.UI;

// TestRoom1 씬의 나가기 버튼, 조인코드 표시 텍스트와 GameSessionManager를 연결한다.

public class TestRoomUI : MonoBehaviour
{
	[Header("참조")]
	[SerializeField] private Button _leaveButton;
	[SerializeField] private TextMeshProUGUI _joinCodeText;

	private void Start()
	{
		_leaveButton.onClick.AddListener(HandleLeaveButtonClicked);
		_joinCodeText.text = GameSessionManager.Instance.JoinCode;
	}

	private void OnDestroy()
	{
		_leaveButton.onClick.RemoveListener(HandleLeaveButtonClicked);
	}

	private void HandleLeaveButtonClicked()
	{
		GameSessionManager.Instance.LeaveSession();
	}
}
