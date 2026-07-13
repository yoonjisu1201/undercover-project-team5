using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 로비 화면의 UI(방 생성 버튼, 조인코드 입력 필드, 참여 버튼)와 GameSessionManager를 코드로 연결한다.

public class LobbyButtonUI : MonoBehaviour
{
	[Header("참조")]
	[SerializeField] private Button _createButton;
	[SerializeField] private Button _joinButton;
	[SerializeField] private TMP_InputField _joinCodeInputField;
	[SerializeField] private TextMeshProUGUI _leaveReasonText;
	[SerializeField] private LoadingOverlayUI _loadingOverlay;

	private void Start()
	{
		_createButton.onClick.AddListener(HandleCreateButtonClicked);
		_joinButton.onClick.AddListener(HandleJoinButtonClicked);

		GameSessionManager.Instance.OnSessionCreated += HandleSessionCreated;
		GameSessionManager.Instance.OnSessionJoined += HandleSessionJoined;
		GameSessionManager.Instance.OnSessionError += HandleSessionError;

		ShowLeaveReasonIfAny();
	}

	// 방에서 로비로 돌아온 경우에만(자진 퇴장/호스트 퇴장/연결 끊김) 사유를 잠깐 보여준다.
	private void ShowLeaveReasonIfAny()
	{
		var reason = GameSessionManager.Instance.LastLeaveReason;
		if (string.IsNullOrEmpty(reason)) return;

		GameSessionManager.Instance.LastLeaveReason = null;
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
		_loadingOverlay.Show();
		GameSessionManager.Instance.CreateSession();
	}

	private void HandleJoinButtonClicked()
	{
		_loadingOverlay.Show();
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
		EventSystem.current.SetSelectedGameObject(null);
	}

	private void HandleSessionError(string message)
	{
		Debug.LogError($"세션 오류: {message}");
	}
}
