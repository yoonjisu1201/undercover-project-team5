using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 라운드 결과(성공/실패)를 표시하고, 확인 버튼을 누르면 대기방으로 돌아가도록 서버에 알린다.
public class RoundResultPanelUI : MonoBehaviour
{
    [SerializeField] private GameObject _panel;
    [SerializeField] private TMP_Text _resultText;
    [SerializeField] private Button _confirmButton;

    private void Start()
    {
        _confirmButton.onClick.AddListener(HandleConfirmButtonClicked);
        RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;

        HandleRoundStateChanged(RoundManager.Instance.CurrentState);
    }

    private void OnDestroy()
    {
        _confirmButton.onClick.RemoveListener(HandleConfirmButtonClicked);

        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }
    }

    private void HandleRoundStateChanged(RoundState state)
    {
        switch (state)
        {
            case RoundState.Success:
                _resultText.text = "검거 성공";
                _panel.SetActive(true);
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                break;
            case RoundState.Fail:
                _resultText.text = "검거 실패";
                _panel.SetActive(true);
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                break;
            default:
                _panel.SetActive(false);
                break;
        }
    }

    private void HandleConfirmButtonClicked()
    {
        RoundManager.Instance.ConfirmResultServerRpc();
    }
}