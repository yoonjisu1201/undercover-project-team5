using TMPro;
using UnityEngine;

// SpectatorUI가 팀원 한 명당 하나씩 갱신하는 목록 행. 무엇을 쓸지는 SpectatorUI가 정하고,
// 이 컴포넌트는 받은 값을 그대로 옮기기만 한다.
public sealed class SpectatorRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _statusText;

    public void Show(string playerName, string status, Color statusColor)
    {
        gameObject.SetActive(true);
        _nameText.text = playerName;
        _statusText.text = status;
        _statusText.color = statusColor;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
