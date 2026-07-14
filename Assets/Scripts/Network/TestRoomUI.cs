using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

// TestRoom1 씬의 나가기 버튼, 조인코드 표시 텍스트와 GameSessionManager를 연결한다.

public class TestRoomUI : MonoBehaviour
{
	[Header("참조")]
	[SerializeField] private Button _leaveButton;
	[SerializeField] private TextMeshProUGUI _joinCodeText;
	[SerializeField] private Button _micMuteButton;
	[SerializeField] private Button _outputMuteButton;
    [SerializeField] private Button _startGameButton;
	
    // 옅은 붉은색(뮤트) / 옅은 녹색(언뮤트): 투명도를 낮춰서 옅게 보이도록 한다.
    private static readonly Color MutedColor = new Color(1f, 0f, 0f, 0.5f);
	private static readonly Color UnmutedColor = new Color(0f, 1f, 0f, 0.5f);

    private bool _isHost;

    private void Start()
	{
		_leaveButton.onClick.AddListener(HandleLeaveButtonClicked);
		_micMuteButton.onClick.AddListener(HandleMicMuteButtonClicked);
		_outputMuteButton.onClick.AddListener(HandleOutputMuteButtonClicked);
        _startGameButton.onClick.AddListener(HandleStartGameButtonClicked);
        _joinCodeText.text = GameSessionManager.Instance.JoinCode;

        _isHost = NetworkManager.Singleton.IsHost;
        _startGameButton.gameObject.SetActive(_isHost); //방장만 스타트 버튼이 보임
        RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged; //임시로 게임시작시 스타트버튼 숨김

        UpdateMicMuteButtonColor();
		UpdateOutputMuteButtonColor();
	}

	private void OnDestroy()
	{
		_leaveButton.onClick.RemoveListener(HandleLeaveButtonClicked);
		_micMuteButton.onClick.RemoveListener(HandleMicMuteButtonClicked);
		_outputMuteButton.onClick.RemoveListener(HandleOutputMuteButtonClicked);
        _startGameButton.onClick.RemoveListener(HandleStartGameButtonClicked);

        if (RoundManager.Instance != null)
		{
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged; //임시로 게임시작시 스타트버튼 숨김
        }
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

    private void HandleStartGameButtonClicked()
	{
        RoundManager.Instance.StartRound1();
    }

    private void HandleRoundStateChanged(RoundState state) //임시로 게임시작시 스타트버튼 숨김
    {
        bool isRoundInProgress = state == RoundState.Round1 || state == RoundState.Round2;
        _startGameButton.gameObject.SetActive(_isHost && !isRoundInProgress);
    }
}
