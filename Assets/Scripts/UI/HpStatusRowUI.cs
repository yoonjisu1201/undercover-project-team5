using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HpStatusUI가 플레이어 한 명당 하나씩 갱신하는 체력바 행(row) 프리팹 스크립트.
public sealed class HpStatusRowUI : MonoBehaviour
{
    [SerializeField] private Image _colorDotImage;
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private Slider _hpSlider; 
    [SerializeField] private GameObject _downedIndicator;

    public void Show(Player player)
    {
        gameObject.SetActive(true);

        PlayerHealth health = player.PlayerHealth;
        bool isDowned = health.IsDowned;

        _colorDotImage.color = player.PlayerColor;
        _nameText.text = player.PlayerName;
        _hpSlider.maxValue = health.MaxHp;
        _hpSlider.value = health.CurrentHp;
        _downedIndicator.SetActive(isDowned);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
