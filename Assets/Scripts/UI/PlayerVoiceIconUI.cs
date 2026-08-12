using UnityEngine;

// 플레이어 한 명의 상태를 아이콘 한 칸에 표시한다.
// 스피커/음소거/해골(다운)이 같은 자리를 공유하므로, 무엇을 띄울지는 이 컴포넌트가 혼자 결정한다.
// 나눠서 관리하면 체력 이벤트와 음성 폴링이 서로 다른 시점에 같은 칸을 덮어써서 깜빡인다.
//
// 체력바(HpStatusRowUI)와 대기방 플레이어 목록(PlayerListPanelUI)이 같이 쓴다.
// 다운 표시가 없는 대기방에서는 아래 다운 관련 필드를 비워 두면 된다.
//
// 표시 우선순위:
//   1. 말하는 중        → 스피커 (다운 상태여도 스피커가 우선)
//   2. 다운             → 해골 + DOWN
//   3. 마이크 끔        → 음소거 아이콘
//   4. 그 외            → 아무것도 표시하지 않음
public sealed class PlayerVoiceIconUI : MonoBehaviour
{
    [SerializeField] private GameObject _speakOnIcon;
    [SerializeField] private GameObject _speakOffIcon;

    [Header("다운 표시 (대기방에서는 비워 둔다)")]
    [SerializeField] private GameObject _downedIcon;
    [SerializeField] private GameObject _downedText;

    // 음성 상태는 이벤트가 없어 짧은 주기로 확인한다. 매 프레임까지는 필요 없다.
    private const float RefreshSeconds = 0.1f;

    private Player _player;
    private float _nextRefreshTime; // 다음 Refresh()를 호출할 Time.unscaledTime. 음성 상태는 UI 연출이므로 unscaledTime을 쓴다.

    // 이 아이콘이 표시할 플레이어를 지정한다. null을 넘기면 Clear()와 같다.
    public void Bind(Player player)
    {
        _player = player;
        Refresh();
    }

    public void Clear()
    {
        _player = null;
        Apply(false, false, false);
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRefreshTime)
        {
            return;
        }

        _nextRefreshTime = Time.unscaledTime + RefreshSeconds;
        Refresh();
    }

    private void Refresh()
    {
        if (_player == null)
        {
            Apply(false, false, false);
            return;
        }

        // 음성 상태는 각 플레이어의 오너가 판단해 NetworkVariable로 올려두므로 여기서는 읽기만 한다.
        bool isMicMuted = _player.IsMicMuted;
        bool isSpeaking = _player.IsSpeaking && !isMicMuted;
        bool isDowned = _player.PlayerHealth != null && _player.PlayerHealth.IsDowned;

        Apply(isMicMuted, isSpeaking, isDowned);    // 표시 우선순위에 따라 아이콘을 켜고 끈다.
    }

    private void Apply(bool isMicMuted, bool isSpeaking, bool isDowned)
    {
        // 다운 표시를 연결하지 않은 곳(대기방)에서는 다운 상태를 아예 고려하지 않는다.
        // 그러지 않으면 다운이 음소거 아이콘을 밀어내고, 정작 띄울 다운 아이콘도 없어서 빈 칸이 된다.
        bool hasDownedIndicator = _downedIcon != null || _downedText != null;

        // DOWN 텍스트는 다운된 동안 계속 띄운다. 자리가 겹치지 않아 스피커와 같이 보여도 된다.
        bool showDownedText = hasDownedIndicator && isDowned;

        // 해골은 스피커와 같은 자리를 쓰므로, 말하는 중에는 스피커에 양보한다.
        // 해골에 가려 누가 말하는지 안 보이면 안 된다.
        bool showDownedIcon = showDownedText && !isSpeaking;

        SetActive(_speakOnIcon, isSpeaking);
        SetActive(_speakOffIcon, !isSpeaking && !showDownedIcon && isMicMuted);
        SetActive(_downedIcon, showDownedIcon);
        SetActive(_downedText, showDownedText);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}
