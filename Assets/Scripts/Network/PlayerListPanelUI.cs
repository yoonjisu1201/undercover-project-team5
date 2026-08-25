using System.Collections.Generic;
using System.ComponentModel;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class PlayerListPanelUI : MonoBehaviour
{
    private const int MaxPlayerCount = 4;

    [Header("UI References")]
    [SerializeField] private TMP_Text _PlayerCountText;
    [SerializeField] private TMP_Text[] _playerNameTexts = new TMP_Text[MaxPlayerCount];
    [SerializeField] private Image[] _stateIconImages = new Image[MaxPlayerCount];
    [SerializeField] private TMP_Text[] _stateTexts = new TMP_Text[MaxPlayerCount];
    [SerializeField] private GameObject[] _localPlayerFrames = new GameObject[MaxPlayerCount];
    // 좌석별 음성 아이콘. 체력바 쪽과 같은 컴포넌트를 쓴다.
    [SerializeField] private PlayerVoiceIconUI[] _voiceIcons = new PlayerVoiceIconUI[MaxPlayerCount];

    [Header("상태 아이콘 스프라이트")]
    [Header("현지화 문구")]
    [SerializeField] private LocalizedString _stateHost;
    [SerializeField] private LocalizedString _stateReady;
    [SerializeField] private LocalizedString _stateNotReady;

    [SerializeField] private Sprite _hostIconSprite;
    [SerializeField] private Sprite _readyIconSprite;
    [SerializeField] private Sprite _notReadyIconSprite;

    [Header("=== RoomManager 넣기 ===")]
    [SerializeField] private WaitingRoomReadyManager _manager;

    private static readonly Color HostColor = Color.yellow;
    private static readonly Color ReadyColor = new Color(0.3f, 0.75f, 0.35f);
    private static readonly Color NotReadyColor = Color.gray;

    // 현재 구독 중인 Player들. 슬롯이 바뀔 때마다 전부 해제하고 현재 슬롯 기준으로 다시 구독한다.
    private readonly List<Player> _subscribedPlayers = new();

    private void Start()
    {
        if (_manager == null)
        {
            Debug.LogWarning("WaitingRoomReadyManager를 찾을 수 없습니다.", this);
            return;
        }

        _manager.Slots.OnListChanged += HandleSlotsChanged;

        // 다른 플레이어들의 PlayerObject가 씬 전환 중이라 아직 재연결되지 않았을 수 있으므로,
        // 준비돼 있으면 바로, 아니면 씬 동기화가 끝난 뒤에 구독/렌더링을 시작한다.
        if (NetworkManager.Singleton.LocalClient?.PlayerObject != null)
        {
            RefreshPlayerSubscriptions();
            Render();
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
        Render();
    }

    private void OnDestroy()
    {
        if (_manager != null)
        {
            _manager.Slots.OnListChanged -= HandleSlotsChanged;
        }

        UnsubscribeAllPlayers();

        // SceneManager는 NetworkManager가 Shutdown되면 null이 된다. 방을 나갈 때가 정확히 그 순서다.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleInitialLoadCompleted;
        }
    }

    private void HandleSlotsChanged(NetworkListEvent<WaitingRoomReadyManager.PlayerSlot> _)
    {
        RefreshPlayerSubscriptions();
        Render();
    }

    // 입장/퇴장으로 슬롯 구성이 바뀔 때마다 호출된다.
    // Role/Name은 _slots가 아니라 Player 쪽 NetworkVariable이라 리스트 이벤트만으로는 알 수 없으므로,
    // 현재 슬롯에 있는 Player들의 변경을 직접 구독해서 감지한다.
    private void RefreshPlayerSubscriptions()
    {
        UnsubscribeAllPlayers();

        foreach (var slot in _manager.Slots)
        {
            Player player = slot.Player;
            player.PlayerNameChanged += HandlePlayerStateChanged;
            _subscribedPlayers.Add(player);
        }
    }

    private void UnsubscribeAllPlayers()
    {
        foreach (var player in _subscribedPlayers)
        {
            player.PlayerNameChanged -= HandlePlayerStateChanged;
        }
        _subscribedPlayers.Clear();
    }
    private void HandlePlayerStateChanged(FixedString32Bytes oldName, FixedString32Bytes newName) => Render();

    private void Render()
    {
        var slots = _manager.Slots;
        int playerCount = slots.Count;
        _PlayerCountText.text = $"({playerCount}/{MaxPlayerCount})";

        for (int i = 0; i < MaxPlayerCount; i++)
        {
            bool hasPlayer = i < playerCount;
            if (!hasPlayer)
            {
                _playerNameTexts[i].text = "";
                _stateIconImages[i].enabled = false;
                _stateTexts[i].text = "";
                ClearVoiceIcon(i);
                SetLocalPlayerMarker(i, false);
                continue;
            }

            // slots[i]가 곧 i번 좌석: WaitingRoomReadyManager가 ClientId 오름차순으로 정렬을 유지해준다.
            var slot = slots[i];
            bool isHost = slot.ClientId == NetworkManager.ServerClientId;

            // 좌석에 있는 플레이어가 로컬 플레이어인지 확인하고, 이름과 상태 아이콘을 업데이트한다.
            bool isLocalPlayer = slot.ClientId == NetworkManager.Singleton.LocalClientId;
            _playerNameTexts[i].text = slot.Player.PlayerName;
            SetLocalPlayerMarker(i, isLocalPlayer);
            _stateIconImages[i].enabled = true;
            _stateIconImages[i].sprite = isHost
                ? _hostIconSprite
                : slot.IsReady ? _readyIconSprite : _notReadyIconSprite;
            _stateTexts[i].text = (isHost
                ? _stateHost
                : slot.IsReady ? _stateReady : _stateNotReady).GetLocalizedString();

            Color stateColor = isHost ? HostColor : slot.IsReady ? ReadyColor : NotReadyColor;
            _stateIconImages[i].color = stateColor;
            _stateTexts[i].color = stateColor;

            if (i < _voiceIcons.Length && _voiceIcons[i] != null)
            {
                _voiceIcons[i].Bind(slot.Player);
            }
        }
    }

    private void SetLocalPlayerMarker(int index, bool isLocalPlayer)
    {
        if (index < _localPlayerFrames.Length && _localPlayerFrames[index] != null)
        {
            _localPlayerFrames[index].SetActive(isLocalPlayer);
        }
    }

    private void ClearVoiceIcon(int index)
    {
        if (index < _voiceIcons.Length && _voiceIcons[index] != null)
        {
            _voiceIcons[index].Clear();
        }
    }
}
