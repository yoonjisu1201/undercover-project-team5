using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerListPanelUI : MonoBehaviour
{
    private const int MaxPlayerCount = 4;

    [Header("UI References")]
    [SerializeField] private TMP_Text _PlayerCountText;
    [SerializeField] private TMP_Text[] _playerNameTexts = new TMP_Text[MaxPlayerCount];
    [SerializeField] private Image[] _stateIconImages = new Image[MaxPlayerCount];
    [SerializeField] private TMP_Text[] _stateTexts = new TMP_Text[MaxPlayerCount];

    [Header("상태 아이콘 스프라이트")]
    [SerializeField] private Sprite _hostIconSprite;
    [SerializeField] private Sprite _readyIconSprite;
    [SerializeField] private Sprite _notReadyIconSprite;
    
    [Header("=== RoomManager 넣기 ===")]
    [SerializeField] private WaitingRoomReadyManager _manager;

    private static readonly Color HostColor = Color.yellow;
    private static readonly Color ReadyColor = new Color(0.3f, 0.75f, 0.35f);
    private static readonly Color NotReadyColor = Color.gray;


    private void Start()
    {
        if (_manager == null)
        {
            Debug.LogWarning("WaitingRoomReadyManager를 찾을 수 없습니다.", this);
            return;
        }

        _manager.Slots.OnListChanged += HandleSlotsChanged;
        Render(); // OnListChanged는 구독 이후의 변경만 알려주므로 현재 상태를 직접 1회 반영
    }

    private void OnDestroy()
    {
        if (_manager != null)
        {
            _manager.Slots.OnListChanged -= HandleSlotsChanged;
        }
    }

    private void HandleSlotsChanged(NetworkListEvent<WaitingRoomReadyManager.PlayerSlot> _)
    {
        Render();
    }

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
                continue;
            }

            // slots[i]가 곧 i번 좌석: WaitingRoomReadyManager가 ClientId 오름차순으로 정렬을 유지해준다.
            var slot = slots[i];
            bool isHost = slot.ClientId == NetworkManager.ServerClientId; // 방장의 로컬 클라이언트 ID는 항상 0
            string roleText = slot.Role == Role.Headquarter ? "HQ" : "Field";
            
            // 실제 이름 기준으로 이름 작성
            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList) {
                if (client.ClientId == slot.ClientId) {
                    client.PlayerObject.TryGetComponent(out Player player);
                    if (player == null) {
                        Debug.LogError($"Player Prefab에 Player Script가 존재하지 않습니다");
                    }

                    _playerNameTexts[i].text = player.PlayerName ?? $"Player {i + 1}";
                }
            }
            _stateIconImages[i].enabled = true;
            _stateIconImages[i].sprite = isHost
                ? _hostIconSprite
                : slot.IsReady ? _readyIconSprite : _notReadyIconSprite;
            _stateTexts[i].text = isHost
                ? $"방장 · {roleText}"
                : slot.IsReady
                    ? $"준비 완료 · {roleText}"
                    : $"준비 중 · {roleText}";

            Color stateColor = isHost ? HostColor : slot.IsReady ? ReadyColor : NotReadyColor;
            _stateIconImages[i].color = stateColor;
            _stateTexts[i].color = stateColor;
        }
    }
}
