using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 음성 오버레이에서 플레이어 한 명을 표시하는 행.
// 이름 왼쪽 칸은 평소 플레이어 고유 색을 보여주고, 다운되면 그 자리를 DOWN 표시로 바꾼다.
// 스피커·음소거는 PlayerVoiceIconUI가 쓰는 별도 칸이라, 다운된 플레이어가 말해도 둘이 함께 보인다.
public sealed class VoiceOverlayRowUI : MonoBehaviour
{
    [Header("=== 이름 왼쪽 칸 (색 점 / 다운 표시가 자리를 공유) ===")]
    [SerializeField] private Image _colorDotImage;
    [SerializeField] private GameObject _downedMark;

    [Header("=== 이름 ===")]
    [SerializeField] private TMP_Text _nameText;

    [Tooltip("평소 이름 색.")]
    [SerializeField] private Color _nameColor = new Color(0.880f, 0.890f, 0.910f, 1f);

    [Tooltip("다운 상태일 때 이름 색. 해골만으로는 눈에 잘 안 띄어 이름으로도 알려 준다.")]
    [SerializeField] private Color _downedNameColor = new Color(0.934f, 0.329f, 0.295f, 1f);

    [Header("=== 스피커 / 음소거 아이콘 ===")]
    [Tooltip("다운 표시는 색 점 자리에서 처리하므로, 이 컴포넌트의 다운 관련 필드는 비워 둔다.")]
    [SerializeField] private PlayerVoiceIconUI _voiceIcon;

    private Player _player;

    // 이 행이 표시하는 플레이어. 오버레이가 목록을 갱신할 때 대조용으로 쓴다.
    public Player Player => _player;

    public void Bind(Player player)
    {
        _player = player;
        gameObject.SetActive(true);

        _nameText.text = player.PlayerName;
        _voiceIcon?.Bind(player);

        Refresh();
    }

    public void Clear()
    {
        _player = null;
        _voiceIcon?.Clear();
        gameObject.SetActive(false);
    }

    // 색·이름·다운 상태는 표시 중에도 바뀔 수 있으므로 오버레이가 갱신할 때 함께 불러 준다.
    public void Refresh()
    {
        if (_player == null)
        {
            return;
        }

        bool isDowned = _player.PlayerHealth != null && _player.PlayerHealth.IsDowned;

        _nameText.text = _player.PlayerName;
        _nameText.color = isDowned ? _downedNameColor : _nameColor;

        // 쓰러진 상태를 이름에도 남긴다. 태그를 넣으면 이름 문자열이 오염되므로 스타일로 켠다.
        _nameText.fontStyle = isDowned
            ? _nameText.fontStyle | TMPro.FontStyles.Strikethrough
            : _nameText.fontStyle & ~TMPro.FontStyles.Strikethrough;

        // 같은 자리를 쓰므로 둘 중 하나만 켠다.
        // Image만 끄면 레이아웃 그룹이 이 칸을 그대로 잡아둬서 해골이 다음 칸으로 밀린다.
        // 자리까지 비우려면 GameObject를 껐다 켜야 한다.
        if (_colorDotImage != null)
        {
            _colorDotImage.color = _player.PlayerColor;
            _colorDotImage.gameObject.SetActive(!isDowned);
        }

        if (_downedMark != null)
        {
            _downedMark.SetActive(isDowned);
        }
    }
}
