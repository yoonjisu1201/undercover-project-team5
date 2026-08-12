using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HpStatusUI가 플레이어 한 명당 하나씩 갱신하는 체력바 행(row) 프리팹 스크립트.
public sealed class HpStatusRowUI : MonoBehaviour
{
    [SerializeField] private Image _colorDotImage;
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private Slider _hpSlider;
    // 스피커·음소거·해골(다운)은 같은 칸을 공유하므로 표시 판단을 이 컴포넌트에 맡긴다.
    // 여기서는 대상 플레이어만 넘겨준다.
    [SerializeField] private PlayerVoiceIconUI _statusIcon;

    public void Show(Player player)
    {
        gameObject.SetActive(true);

        PlayerHealth health = player.PlayerHealth;

        _colorDotImage.color = player.PlayerColor;
        _nameText.text = player.PlayerName;
        _hpSlider.maxValue = health.MaxHp;
        _hpSlider.value = health.CurrentHp;

        if (_statusIcon != null)
        {
            _statusIcon.Bind(player);
        }
    }

    public void Hide()
    {
        if (_statusIcon != null)
        {
            _statusIcon.Clear();
        }

        gameObject.SetActive(false);
    }
}
