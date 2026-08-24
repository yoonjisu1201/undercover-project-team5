using System;
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
	[SerializeField] private TextMeshProUGUI _roomNameText;
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
    [SerializeField] private TextMeshProUGUI _nicknameNoticeText; // "이미 사용 중인 닉네임입니다" 안내

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

    // 월드 준비·시작 버튼이 기존 Canvas UI와 같은 상태를 사용하도록 읽기 전용 상태와 변경 신호를 제공한다.
    // 네트워크 준비 로직을 월드 버튼에 중복하지 않아 호스트·참가자 규칙의 기준을 WaitingRoomUI 한 곳에 둔다.
    public event Action ReadyStartStateChanged;
    public bool IsHost => _isHost;
    public bool IsReady => _isReady;
    public bool CanUseReadyStart => !_isHost || _readyManager.CanStart;
    public bool IsReadyStartActive => _isHost ? _readyManager.CanStart : _isReady;

    private static bool s_hasCompletedNicknameSetup;
    private static string s_savedNickname;

    private bool _isNicknamePanelOpen;
    // 이름 요청 결과를 구독한 Player. OnDestroy에서 해제하려면 대상을 기억해야 한다.
    private Player _boundPlayer;

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
        // 씬에 값을 저장해두면 모든 플레이어에게 같은 이름이 보인다. 접속은 끝난 상태라 자기 기본 이름을 알 수 있다.
        _nicknameInputField.text = GetCurrentNickname();
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

        _nicknameNoticeText.gameObject.SetActive(false);

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

        // 오브젝트 활성화 순서와 관계없이 초기 호스트·준비 상태가 결정된 뒤 월드 버튼에도 현재 상태를 알린다.
        ReadyStartStateChanged?.Invoke();

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

        if (_boundPlayer != null) { _boundPlayer.NameRequestResolved -= HandleNameRequestResolved; }

        GameplayUiMode.Instance?.UnregisterUi(this);

        // 설정창을 연 채로 씬이 바뀌면 카운트가 남아 다음 씬에서 조작이 계속 막힌다.
        if (_isNicknamePanelOpen)
        {
            _isNicknamePanelOpen = false;
            GameplayUiMode.Instance?.DeactivateCursor();
        }

        if (GameSessionManager.Instance != null)
        {
            GameSessionManager.Instance.OnSessionJoined -= UpdateJoinCodeText;
        }

        if (_readyManager != null)
        {
            _readyManager.Slots.OnListChanged -= HandleSlotsChanged;
        }

        // SceneManager는 NetworkManager가 Shutdown되면 null이 된다. 방을 나갈 때가 정확히 그 순서다.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleInitialLoadCompleted;
        }
    }

    private void UpdateJoinCodeText()
    {
        _joinCodeText.text = GameSessionManager.Instance.JoinCode;
        _roomNameText.text = GameSessionManager.Instance.RoomName;
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

	// Canvas 나가기 버튼과 월드 출구 문이 동일한 세션 종료 흐름을 재사용할 수 있도록 공개한다.
	public void HandleLeaveButtonClicked()
	{
		GameSessionManager.Instance.LeaveSession();
	}

    // ESC로 닫으면 이름 적용 없이 닉네임 패널을 취소(닫기)한다. (IClosableUi)
    public void Close()
    {
        SetNicknamePanelOpen(false);
    }

    // 설정창을 여닫을 때 ESC 스택 등록과 조작 차단을 한 번에 처리한다.
    // 참조 카운트 방식이라 열고 닫는 짝이 어긋나면 조작이 계속 막힌다.
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
            // 이동과 시야 회전을 함께 막는다. 대기방은 씬 기본값이 '커서 보임'이라
            // 닫을 때 커서 상태가 그대로 유지된다.
            GameplayUiMode.Instance?.ActivateCursor();
            return;
        }

        // 어떤 경로로 닫아도(확인·ESC·닫기 버튼) 다시 묻지 않도록 여기서 한 번에 표시한다.
        s_hasCompletedNicknameSetup = true;

        GameplayUiMode.Instance?.UnregisterUi(this);
        GameplayUiMode.Instance?.DeactivateCursor();
    }

    private void HandleNicknameConfirmButtonClicked()
    {
        NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;

        if (localPlayerObject.TryGetComponent(out Player localPlayer))
        {
            _nicknameNoticeText.gameObject.SetActive(false);

            // 다시 누를 때 구독이 쌓이지 않도록 먼저 해제한다.
            localPlayer.NameRequestResolved -= HandleNameRequestResolved;
            localPlayer.NameRequestResolved += HandleNameRequestResolved;
            _boundPlayer = localPlayer;

            // 서버가 중복을 판정하므로 결과가 올 때까지 패널을 닫지 않는다.
            localPlayer.SetPlayerName(_nicknameInputField.text);
        }
    }

    // 확정 버튼으로 보낸 요청만 여기로 온다. 저장된 닉네임 자동 적용(TryApplySavedNickname)은
    // 구독하지 않는 경로라, 그쪽이 거절되면 기본 이름으로 남고 안내는 뜨지 않는다.
    private void HandleNameRequestResolved(bool accepted)
    {
        // 확정 버튼을 눌러 여기까지 왔다면 패널은 이미 열려 있으므로 안내만 켜면 된다.
        if (!accepted)
        {
            _nicknameNoticeText.gameObject.SetActive(true);
            return;
        }

        SaveNicknameIfValid(_nicknameInputField.text);
        SetNicknamePanelOpen(false);
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

        // PlayerObject가 아직 안 왔어도 접속은 끝난 상태라, 같은 규칙으로 기본 이름을 만든다.
        return Player.GetDefaultName(NetworkManager.Singleton.LocalClientId);
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

    // 하나의 월드 스테이션이 호스트에게는 게임 시작, 참가자에게는 준비 토글로 동작하게 역할 분기를 모은다.
    // 분기를 UI에 유지해 월드 버튼이 네트워크 준비·시작 구현 세부사항에 의존하지 않게 한다.
    public void InteractReadyStart()
    {
        if (_isHost)
        {
            HandleStartGameButtonClicked();
            return;
        }

        HandleReadyButtonClicked();
    }

    private void HandleReadyButtonClicked()
    {
        _isReady = !_isReady;
        _readyManager.SetReadyServerRpc(_isReady);
        UpdateReadyButtonColor();

        // Canvas 버튼을 먼저 갱신한 뒤 월드 버튼에도 같은 로컬 준비 상태를 알린다.
        ReadyStartStateChanged?.Invoke();
    }

    private void UpdateReadyButtonColor()
    {
        _readyButton.targetGraphic.color = _isReady ? ReadyColor : NotReadyColor;
    }

    // 입장·퇴장·준비 변경은 호스트의 시작 가능 여부를 바꾸므로 Canvas와 월드 시작 버튼을 함께 갱신한다.
    // 참가자의 월드 버튼은 자신의 준비 토글에서 갱신되므로 여기서는 호스트 상태만 처리한다.
    private void HandleSlotsChanged(NetworkListEvent<WaitingRoomReadyManager.PlayerSlot> _)
    {
        if (!_isHost)
        {
            return;
        }

        UpdateStartButtonAndText();
        ReadyStartStateChanged?.Invoke();
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
