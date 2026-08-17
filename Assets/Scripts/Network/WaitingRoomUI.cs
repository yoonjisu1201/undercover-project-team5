using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.SceneManagement;

// TestRoom1 씬의 나가기 버튼, 조인코드 표시 텍스트와 GameSessionManager를 연결한다.
public class WaitingRoomUI : MonoBehaviour, IClosableUi
{
	[Header("참조")]
	[SerializeField] private Button _leaveButton;
	[SerializeField] private TextMeshProUGUI _joinCodeText;
	[SerializeField] private Button _micMuteButton;
	[SerializeField] private Button _outputMuteButton;
    [SerializeField] private Button _startGameButton;
    [SerializeField] private TextMeshProUGUI _startGameButtonInfoText;
    [SerializeField] private Button _readyButton;
    [SerializeField] private WaitingRoomReadyManager _readyManager;

    [Header("=== 닉네임 설정 ===")]
    [SerializeField] private TMP_InputField _nicknameInputField;
    [SerializeField] private Button _nicknameConfirmButton;
    [SerializeField] private GameObject _nicknameSettingPanel;

    [Header("=== 조인코드 복사 ===")]
    [SerializeField] private TextMeshProUGUI _copyNoticeText; // "복사되었습니다" 안내, 잠깐 표시

    private const float CopyNoticeSeconds = 1.5f;

    [Header("=== 사용될 LocalizedString ===")]
    [SerializeField] private LocalizedString _startInfoAllReady;
    [SerializeField] private LocalizedString _startInfoNeedsMorePlayer;

    // 옅은 붉은색(뮤트) / 옅은 녹색(언뮤트): 투명도를 낮춰서 옅게 보이도록 한다.
    private static readonly Color MutedColor = new Color(1f, 0f, 0f, 0.5f);
	private static readonly Color UnmutedColor = new Color(0f, 1f, 0f, 0.5f);

    // 준비 전(흰색, 나가기 버튼과 동일) / 준비 완료(옅은 초록) 버튼 색상
    private static readonly Color NotReadyColor = Color.white;
    private static readonly Color ReadyColor = new Color(0f, 1f, 0f, 0.5f);

    private bool _isHost;
    private bool _isReady;
    private static bool s_hasCompletedNicknameSetup;
    private static string s_savedNickname;

    private bool _isNicknamePanelOpen;

    // 현재 구독 중인 Player들의 역할 변경. 슬롯이 바뀔 때마다 전부 해제하고 현재 슬롯 기준으로 다시 구독한다.
    private readonly List<Player> _subscribedPlayers = new();

    // 매 갱신마다 GetComponent를 반복 호출하지 않도록 Start에서 1회 캐싱한다.
    private LocalizeStringEvent _startGameButtonInfoLocalize;

    private void Start()
	{
        // LocalizeStringEvent가 붙어있지 않으면 이후 갱신 시 NRE가 나므로,
        // 캐싱 시점에 미리 확인해 원인을 바로 알 수 있게 경고를 남긴다.
        _startGameButtonInfoLocalize = GetRequiredLocalizeStringEvent(_startGameButtonInfoText);

		_leaveButton.onClick.AddListener(HandleLeaveButtonClicked);
		_micMuteButton.onClick.AddListener(HandleMicMuteButtonClicked);
		_outputMuteButton.onClick.AddListener(HandleOutputMuteButtonClicked);
        _startGameButton.onClick.AddListener(HandleStartGameButtonClicked);
        _readyButton.onClick.AddListener(HandleReadyButtonClicked);

        _nicknameInputField.characterLimit = Player.MaxPlayerNameLength;
        _nicknameConfirmButton.onClick.AddListener(HandleNicknameConfirmButtonClicked);

        bool shouldShowNicknamePanel = !s_hasCompletedNicknameSetup;
        SetNicknamePanelOpen(shouldShowNicknamePanel);

        if (!shouldShowNicknamePanel)
        {
            TryApplySavedNickname();
        }

        UpdateJoinCodeText();
        GameSessionManager.Instance.OnSessionJoined += UpdateJoinCodeText; // 조인 완료가 씬 로드보다 늦을 때를 대비한 재확인용

        if (_copyNoticeText != null)
        {
            _copyNoticeText.gameObject.SetActive(false);
        }

        _isHost = NetworkManager.Singleton.IsHost;
        _startGameButton.gameObject.SetActive(_isHost); //방장만 스타트 버튼이 보임
        _startGameButtonInfoText.gameObject.SetActive(_isHost); // 방장만 스타트 가능 여부 텍스트가 보임
        _readyButton.gameObject.SetActive(!_isHost); //방장이 아닐 때만 준비 버튼이 보임

        _readyManager.Slots.OnListChanged += HandleSlotsChanged;

        if (_isHost)
        {
            UpdateStartButtonAndText(); // 이벤트는 구독 이후 변경만 알려주므로 현재 상태를 직접 1회 반영
        }
        else
        {
            UpdateReadyButtonColor();
        }

        UpdateMicMuteButtonColor();
        UpdateOutputMuteButtonColor();

        // 다른 플레이어들의 PlayerObject가 씬 전환 중이라 아직 재연결되지 않았을 수 있으므로,
        // 준비돼 있으면 바로, 아니면 씬 동기화가 끝난 뒤에 구독/역할 선택 UI를 초기화한다.
        if (NetworkManager.Singleton.LocalClient?.PlayerObject != null)
        {
            TryApplySavedNickname();
        }
        else
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleInitialLoadCompleted;
        }
	}

    private void HandleInitialLoadCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleInitialLoadCompleted;
        TryApplySavedNickname();
    }

	private void OnDestroy()
	{
		_leaveButton.onClick.RemoveListener(HandleLeaveButtonClicked);
		_micMuteButton.onClick.RemoveListener(HandleMicMuteButtonClicked);
		_outputMuteButton.onClick.RemoveListener(HandleOutputMuteButtonClicked);
        _startGameButton.onClick.RemoveListener(HandleStartGameButtonClicked);
        _readyButton.onClick.RemoveListener(HandleReadyButtonClicked);

        _nicknameConfirmButton.onClick.RemoveListener(HandleNicknameConfirmButtonClicked);

        GameplayUiMode.Instance?.UnregisterUi(this);

        // 설정창을 연 채로 씬이 바뀌면 차단 카운트가 남아 다음 씬에서 이동이 계속 막힌다.
        if (_isNicknamePanelOpen)
        {
            _isNicknamePanelOpen = false;
            GameplayUiMode.Instance?.PopMovementBlock();
        }

        if (GameSessionManager.Instance != null)
        {
            GameSessionManager.Instance.OnSessionJoined -= UpdateJoinCodeText;
        }

        if (_readyManager != null)
        {
            _readyManager.Slots.OnListChanged -= HandleSlotsChanged;
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleInitialLoadCompleted;
        }
    }

    private void UpdateJoinCodeText()
    {
        _joinCodeText.text = GameSessionManager.Instance.JoinCode;
    }

    // 조인코드를 클립보드에 복사하고 잠깐 안내를 띄운다.
    // 조인코드 복사 버튼의 OnClick에 연결한다.
    public void OnCopyJoinCodeButtonClicked()
    {
        string joinCode = GameSessionManager.Instance.JoinCode;
        if (string.IsNullOrEmpty(joinCode)) return; // 아직 발급 전이면 복사할 게 없다

        GUIUtility.systemCopyBuffer = joinCode;
        ShowCopyNotice();
    }

    private void ShowCopyNotice()
    {
        if (_copyNoticeText == null) return;

        CancelInvoke(nameof(HideCopyNotice)); // 연속으로 눌러도 마지막 클릭 기준으로 유지된다
        _copyNoticeText.gameObject.SetActive(true);
        Invoke(nameof(HideCopyNotice), CopyNoticeSeconds);
    }

    private void HideCopyNotice()
    {
        _copyNoticeText.gameObject.SetActive(false);
    }

    private LocalizeStringEvent GetRequiredLocalizeStringEvent(TextMeshProUGUI text)
    {
        LocalizeStringEvent localize = text.GetComponent<LocalizeStringEvent>();
        if (localize == null)
        {
            Debug.LogWarning($"[WaitingRoomUI] {text.name}에 LocalizeStringEvent가 없어 텍스트를 갱신할 수 없습니다.");
        }
        return localize;
    }

	private void HandleLeaveButtonClicked()
	{
		GameSessionManager.Instance.LeaveSession();
	}

    // ESC로 닫으면 이름 적용 없이 닉네임 패널을 취소(닫기)한다. (IClosableUi)
    public void Close()
    {
        s_hasCompletedNicknameSetup = true;
        SetNicknamePanelOpen(false);
    }

    // 설정창을 여닫을 때 ESC 스택 등록과 이동 차단을 한 번에 처리한다.
    // 이동 차단은 참조 카운트 방식이라 열고 닫는 짝이 어긋나면 이동이 계속 막힌다.
    private void SetNicknamePanelOpen(bool open)
    {
        _nicknameSettingPanel.SetActive(open);

        if (_isNicknamePanelOpen == open)
        {
            return;
        }

        _isNicknamePanelOpen = open;

        if (open)
        {
            // ESC 닫기 스택에 등록한다. (ESC 시 설정창보다 먼저 닫히도록)
            GameplayUiMode.Instance?.RegisterUi(this);
            // 대기방은 커서가 계속 보여야 하므로 커서는 건드리지 않고 이동만 막는다.
            GameplayUiMode.Instance?.PushMovementBlock();
            return;
        }

        GameplayUiMode.Instance?.UnregisterUi(this);
        GameplayUiMode.Instance?.PopMovementBlock();
    }

    private void HandleNicknameConfirmButtonClicked()
    {
        NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;

        if (localPlayerObject.TryGetComponent(out Player localPlayer))
        {
            localPlayer.SetPlayerName(_nicknameInputField.text);
            SaveNicknameIfValid(_nicknameInputField.text);
            s_hasCompletedNicknameSetup = true;
            SetNicknamePanelOpen(false);
        }
    }

    // 설정창을 다시 열어 닉네임을 바꾼다. 지금 쓰는 이름을 입력창에 미리 채워 준다.
    // 닉네임 변경 버튼의 OnClick에 연결한다.
    public void OnNicknameChangeButtonClicked()
    {
        if (_nicknameSettingPanel.activeSelf)
        {
            return;
        }

        _nicknameInputField.text = GetCurrentNickname();
        SetNicknamePanelOpen(true);
        _nicknameInputField.Select();
    }

    private string GetCurrentNickname()
    {
        if (!string.IsNullOrWhiteSpace(s_savedNickname))
        {
            return s_savedNickname;
        }

        NetworkObject localPlayerObject = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        Player localPlayer;
        if (localPlayerObject != null && localPlayerObject.TryGetComponent(out localPlayer))
        {
            return localPlayer.PlayerName;
        }

        return string.Empty;
    }

    private void TryApplySavedNickname()
    {
        if (string.IsNullOrWhiteSpace(s_savedNickname)) return;
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject == null) return;

        NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localPlayerObject.TryGetComponent(out Player localPlayer))
        {
            localPlayer.SetPlayerName(s_savedNickname);
        }
    }

    private static void SaveNicknameIfValid(string nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname)) return;

        s_savedNickname = nickname.Length > Player.MaxPlayerNameLength
            ? nickname.Substring(0, Player.MaxPlayerNameLength)
            : nickname;
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
        _readyManager.SetReadyServerRpc(_isReady);
        UpdateReadyButtonColor();
    }

    private void UpdateReadyButtonColor()
    {
        _readyButton.targetGraphic.color = _isReady ? ReadyColor : NotReadyColor;
    }

    // 입장/퇴장/준비 상태 변경으로 슬롯 구성이 바뀔 때마다 호출된다.
    private void HandleSlotsChanged(NetworkListEvent<WaitingRoomReadyManager.PlayerSlot> _)
    {
        if (_isHost)
        {
            UpdateStartButtonAndText();
        }
    }

    // 시작 버튼 및 알림 텍스트 갱신한다.
    private void UpdateStartButtonAndText()
    {
	    // 시작 가능한지 확인
        _startGameButton.interactable = _readyManager.CanStart; 

        // 시작 가능한 경우 설명 텍스트 제거
        if (_readyManager.CanStart) {
	        _startGameButtonInfoText.text = "";
	        return;
        }

        // 시작 불가능한 이유를 안내한다.
        if (!_readyManager.HasEnoughPlayers) {
	        // 사람 부족해서 시작 못하는 경우
	        _startGameButtonInfoLocalize.StringReference = _startInfoNeedsMorePlayer;
	        _startGameButtonInfoLocalize.StringReference.Arguments = new object[] { WaitingRoomReadyManager.MinPlayersToStart };
        } else if (!_readyManager.IsAllReady) {
	        // 레디 다 안해서 시작 못하는 경우
	        _startGameButtonInfoLocalize.StringReference = _startInfoAllReady;
        }
        
        _startGameButtonInfoLocalize.RefreshString();
    }

    private void HandleStartGameButtonClicked()
	{
        GameSessionManager.Instance.StartGame();
    }
}
