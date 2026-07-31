using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.SceneManagement;

// TestRoom1 씬의 나가기 버튼, 조인코드 표시 텍스트와 GameSessionManager를 연결한다.

public class WaitingRoomUI : MonoBehaviour
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

    [Header("=== 역할 선택 관련 ===")]
    [SerializeField] private Button _fieldRoleButton;
    [SerializeField] private Button _headquartersRoleButton;
    [SerializeField] private TextMeshProUGUI _selectedRoleText;
    [SerializeField] private TextMeshProUGUI _headquartersAvailabilityText;

    [Header("=== 사용될 LocalizedString ===")]
    [SerializeField] private LocalizedString _fieldSelectedText;
    [SerializeField] private LocalizedString _headquarterSelectedText;
    [SerializeField] private LocalizedString _hqAlreadyExistsText;
    [SerializeField] private LocalizedString _hqAvailableText;
    [SerializeField] private LocalizedString _startInfoNeedsHq;
    [SerializeField] private LocalizedString _startInfoAllReady;
    [SerializeField] private LocalizedString _startInfoNeedsMorePlayer;

    // 옅은 붉은색(뮤트) / 옅은 녹색(언뮤트): 투명도를 낮춰서 옅게 보이도록 한다.
    private static readonly Color MutedColor = new Color(1f, 0f, 0f, 0.5f);
	private static readonly Color UnmutedColor = new Color(0f, 1f, 0f, 0.5f);

    // 준비 전(흰색, 나가기 버튼과 동일) / 준비 완료(옅은 초록) 버튼 색상
    private static readonly Color NotReadyColor = Color.white;
    private static readonly Color ReadyColor = new Color(0f, 1f, 0f, 0.5f);
    private static readonly Color UnselectedRoleColor = new Color(0.2f, 0.24f, 0.32f, 1f);
    private static readonly Color FieldRoleColor = new Color(0.13f, 0.52f, 0.86f, 1f);
    private static readonly Color HeadquartersRoleColor = new Color(0.95f, 0.58f, 0.15f, 1f);
    private static readonly Color HeadquartersUnavailableColor = new Color(0.95f, 0.4f, 0.4f, 1f);

    private bool _isHost;
    private bool _isReady;

    // 현재 구독 중인 Player들의 역할 변경. 슬롯이 바뀔 때마다 전부 해제하고 현재 슬롯 기준으로 다시 구독한다.
    private readonly List<Player> _subscribedPlayers = new();

    // 매 갱신마다 GetComponent를 반복 호출하지 않도록 Start에서 1회 캐싱한다.
    private LocalizeStringEvent _selectedRoleLocalize;
    private LocalizeStringEvent _headquartersAvailabilityLocalize;
    private LocalizeStringEvent _startGameButtonInfoLocalize;

    private void Start()
	{
        // LocalizeStringEvent가 붙어있지 않으면 이후 갱신 시 NRE가 나므로,
        // 캐싱 시점에 미리 확인해 원인을 바로 알 수 있게 경고를 남긴다.
        _selectedRoleLocalize = GetRequiredLocalizeStringEvent(_selectedRoleText);
        _headquartersAvailabilityLocalize = GetRequiredLocalizeStringEvent(_headquartersAvailabilityText);
        _startGameButtonInfoLocalize = GetRequiredLocalizeStringEvent(_startGameButtonInfoText);

		_leaveButton.onClick.AddListener(HandleLeaveButtonClicked);
		_micMuteButton.onClick.AddListener(HandleMicMuteButtonClicked);
		_outputMuteButton.onClick.AddListener(HandleOutputMuteButtonClicked);
        _startGameButton.onClick.AddListener(HandleStartGameButtonClicked);
        _readyButton.onClick.AddListener(HandleReadyButtonClicked);
        _fieldRoleButton.onClick.AddListener(HandleFieldRoleButtonClicked);
        _headquartersRoleButton.onClick.AddListener(HandleHeadquartersRoleButtonClicked);
        UpdateJoinCodeText();
        GameSessionManager.Instance.OnSessionJoined += UpdateJoinCodeText; // 조인 완료가 씬 로드보다 늦을 때를 대비한 재확인용

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
            RefreshPlayerSubscriptions();
            UpdateRoleSelection();
        }
        else
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleInitialLoadCompleted;
        }
	}

    private void HandleInitialLoadCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleInitialLoadCompleted;
        RefreshPlayerSubscriptions();
        UpdateRoleSelection();
    }

	private void OnDestroy()
	{
		_leaveButton.onClick.RemoveListener(HandleLeaveButtonClicked);
		_micMuteButton.onClick.RemoveListener(HandleMicMuteButtonClicked);
		_outputMuteButton.onClick.RemoveListener(HandleOutputMuteButtonClicked);
        _startGameButton.onClick.RemoveListener(HandleStartGameButtonClicked);
        _readyButton.onClick.RemoveListener(HandleReadyButtonClicked);
        _fieldRoleButton.onClick.RemoveListener(HandleFieldRoleButtonClicked);
        _headquartersRoleButton.onClick.RemoveListener(HandleHeadquartersRoleButtonClicked);

        if (GameSessionManager.Instance != null)
        {
            GameSessionManager.Instance.OnSessionJoined -= UpdateJoinCodeText;
        }

        if (_readyManager != null)
        {
            _readyManager.Slots.OnListChanged -= HandleSlotsChanged;
        }
        UnsubscribeAllPlayers();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleInitialLoadCompleted;
        }
    }

    private void UpdateJoinCodeText()
    {
        _joinCodeText.text = GameSessionManager.Instance.JoinCode;
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

    private void HandleSlotsChanged(NetworkListEvent<WaitingRoomReadyManager.PlayerSlot> _)
    {
        RefreshPlayerSubscriptions();
        RefreshRoleDependentUI();
    }

    // 입장/퇴장으로 슬롯 구성이 바뀔 때마다 호출된다.
    // 본부 선택 가능 여부(IsHeadquartersAvailableFor)와 시작 가능 여부(HaveHqAgent)가
    // 다른 플레이어의 역할에 따라 달라지므로, 현재 슬롯에 있는 Player들의 역할 변경을 직접 구독해서 감지한다.
    private void RefreshPlayerSubscriptions()
    {
        UnsubscribeAllPlayers();

        foreach (var slot in _readyManager.Slots)
        {
            Player player = slot.Player;
            player.PlayerRoleChanged += HandlePlayerRoleChanged;
            _subscribedPlayers.Add(player);
        }
    }

    private void UnsubscribeAllPlayers()
    {
        foreach (var player in _subscribedPlayers)
        {
            player.PlayerRoleChanged -= HandlePlayerRoleChanged;
        }
        _subscribedPlayers.Clear();
    }

    private void HandlePlayerRoleChanged(Role oldRole, Role newRole) => RefreshRoleDependentUI();

    private void RefreshRoleDependentUI()
    {
        if (_isHost)
        {
            UpdateStartButtonAndText();
        }

        UpdateRoleSelection();
    }

    private void HandleFieldRoleButtonClicked()
    {
        _readyManager.SetRoleServerRpc(Role.Field);
    }

    private void HandleHeadquartersRoleButtonClicked()
    {
        _readyManager.SetRoleServerRpc(Role.Headquarter);
    }

    // Slot(접속된 유저들의 상태)가 변경될때마다 호출된다.
    private void UpdateRoleSelection()
    {
        if (!NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out Player player)) {
	        Debug.LogError($"[UpdateRoleSelection] 잘못된 Player의 요청입니다");
	        return;
        }

        Role selectedRole = player.PlayerRole;

        bool isField = selectedRole == Role.Field;
        bool headquartersAvailable = _readyManager.IsHeadquartersAvailableFor(NetworkManager.Singleton.LocalClientId);

        _fieldRoleButton.targetGraphic.color = isField ? FieldRoleColor : UnselectedRoleColor;
        _headquartersRoleButton.targetGraphic.color = isField ? UnselectedRoleColor : HeadquartersRoleColor;
        _headquartersRoleButton.interactable = headquartersAvailable;

        // 선택된 역할과 현재 게임 상태에 맞게 선택된 역할 텍스트, 본부 선택 가능 여부 텍스트 갱신하기
        if (_selectedRoleLocalize != null)
        {
            _selectedRoleLocalize.StringReference = isField ? _fieldSelectedText : _headquarterSelectedText;
            _selectedRoleLocalize.RefreshString();
        }
        _selectedRoleText.color = isField ? FieldRoleColor : HeadquartersRoleColor;

        if (_headquartersAvailabilityLocalize != null)
        {
            _headquartersAvailabilityLocalize.StringReference = headquartersAvailable
                ? _hqAvailableText
                : _hqAlreadyExistsText;
            _headquartersAvailabilityLocalize.RefreshString();
        }
        _headquartersAvailabilityText.color = headquartersAvailable
            ? Color.white
            : HeadquartersUnavailableColor;
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
        } else if (!_readyManager.HaveHqAgent) {
	        // 본부 요원 없어서 시작 못하는 경우
	        _startGameButtonInfoLocalize.StringReference = _startInfoNeedsHq;
        }
        
        _startGameButtonInfoLocalize.RefreshString();
    }

    private void HandleStartGameButtonClicked()
	{
        GameSessionManager.Instance.StartGame();
    }
}
