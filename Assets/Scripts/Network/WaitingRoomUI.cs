using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

// TestRoom1 씬의 나가기 버튼, 조인코드 표시 텍스트와 GameSessionManager를 연결한다.

public class WaitingRoomUI : MonoBehaviour
{
	[Header("참조")]
	[SerializeField] private Button _leaveButton;
	[SerializeField] private TextMeshProUGUI _joinCodeText;
	[SerializeField] private Button _micMuteButton;
	[SerializeField] private Button _outputMuteButton;
    [SerializeField] private Button _startGameButton;
    [SerializeField] private Button _readyButton;

    // 옅은 붉은색(뮤트) / 옅은 녹색(언뮤트): 투명도를 낮춰서 옅게 보이도록 한다.
    private static readonly Color MutedColor = new Color(1f, 0f, 0f, 0.5f);
	private static readonly Color UnmutedColor = new Color(0f, 1f, 0f, 0.5f);

    // 준비 전(옅은 회색) / 준비 완료(옅은 초록) 버튼 색상
    private static readonly Color NotReadyColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
    private static readonly Color ReadyColor = new Color(0f, 1f, 0f, 0.5f);

    private bool _isHost;
    private bool _isReady;

    private void Start()
	{
		_leaveButton.onClick.AddListener(HandleLeaveButtonClicked);
		_micMuteButton.onClick.AddListener(HandleMicMuteButtonClicked);
		_outputMuteButton.onClick.AddListener(HandleOutputMuteButtonClicked);
        _startGameButton.onClick.AddListener(HandleStartGameButtonClicked);
        _readyButton.onClick.AddListener(HandleReadyButtonClicked);
        UpdateJoinCodeText();
        GameSessionManager.Instance.OnSessionJoined += UpdateJoinCodeText; // 조인 완료가 씬 로드보다 늦을 때를 대비한 재확인용

        _isHost = NetworkManager.Singleton.IsHost;
        _startGameButton.gameObject.SetActive(_isHost); //방장만 스타트 버튼이 보임
        _readyButton.gameObject.SetActive(!_isHost); //방장이 아닐 때만 준비 버튼이 보임

        if (_isHost)
        {
            WaitingRoomReadyManager.Instance.Slots.OnListChanged += HandleSlotsChanged;
            UpdateStartButtonInteractable(); // OnListChanged는 구독 이후 변경만 알려주므로 현재 상태를 직접 1회 반영
        }
        else
        {
            UpdateReadyButtonColor();
        }

        UpdateMicMuteButtonColor();
		UpdateOutputMuteButtonColor();
	}

	private void OnDestroy()
	{
		_leaveButton.onClick.RemoveListener(HandleLeaveButtonClicked);
		_micMuteButton.onClick.RemoveListener(HandleMicMuteButtonClicked);
		_outputMuteButton.onClick.RemoveListener(HandleOutputMuteButtonClicked);
        _startGameButton.onClick.RemoveListener(HandleStartGameButtonClicked);
        _readyButton.onClick.RemoveListener(HandleReadyButtonClicked);

        if (GameSessionManager.Instance != null)
        {
            GameSessionManager.Instance.OnSessionJoined -= UpdateJoinCodeText;
        }

        if (_isHost && WaitingRoomReadyManager.Instance != null)
        {
            WaitingRoomReadyManager.Instance.Slots.OnListChanged -= HandleSlotsChanged;
        }
    }

    private void UpdateJoinCodeText()
    {
        _joinCodeText.text = GameSessionManager.Instance.JoinCode;
    }

	private void HandleLeaveButtonClicked()
	{
		GameSessionManager.Instance.LeaveSession();
	}

	private void HandleMicMuteButtonClicked()
	{
		VivoxManager.Instance.ToggleMicMute();
		UpdateMicMuteButtonColor();
	}

	private void UpdateMicMuteButtonColor()
	{
		_micMuteButton.targetGraphic.color = VivoxManager.IsMicMuted ? MutedColor : UnmutedColor;
	}

	private void HandleOutputMuteButtonClicked()
	{
		VivoxManager.Instance.ToggleOutputMute();
		UpdateOutputMuteButtonColor();
	}

	private void UpdateOutputMuteButtonColor()
	{
		_outputMuteButton.targetGraphic.color = VivoxManager.IsOutputMuted ? MutedColor : UnmutedColor;
	}

    private void HandleReadyButtonClicked()
    {
        _isReady = !_isReady;
        WaitingRoomReadyManager.Instance.SetReadyServerRpc(_isReady);
        UpdateReadyButtonColor();
    }

    private void UpdateReadyButtonColor()
    {
        _readyButton.targetGraphic.color = _isReady ? ReadyColor : NotReadyColor;
    }

    private void HandleSlotsChanged(NetworkListEvent<WaitingRoomReadyManager.PlayerSlot> _)
    {
        UpdateStartButtonInteractable();
    }

    private void UpdateStartButtonInteractable()
    {
        _startGameButton.interactable = WaitingRoomReadyManager.Instance.CanStart;
    }

    private void HandleStartGameButtonClicked()
	{
        GameSessionManager.Instance.StartGame();
    }
}
