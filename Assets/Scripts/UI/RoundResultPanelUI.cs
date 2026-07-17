using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 라운드 결과(1라운드 클리어/성공/실패)를 표시하고, 확인 버튼을 누르면 대기방으로 돌아가도록 서버에 알린다.
public class RoundResultPanelUI : MonoBehaviour
{
    [SerializeField] private GameObject _panel;
    [SerializeField] private TMP_Text _resultText;
    [SerializeField] private Button _confirmButton;
    [SerializeField] private GameObject _inventoryCanvas;
    [SerializeField] private TMP_Text _countdownText;
    [SerializeField] private GameObject _nextRoundText;
    [SerializeField] private TMP_Text _confirmedCountText; // "확인한 인원/총 인원" 표시용

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

    private void Update()
    {
        if (RoundManager.Instance == null) return;

        var state = RoundManager.Instance.CurrentState;

        if (state == RoundState.Round1Clear)
        {
            // 1라운드 클리어: 2라운드 자동 시작까지 남은 시간 표시
            int remaining = Mathf.CeilToInt(RoundManager.Instance.GetRemainingTime());
            _countdownText.text = remaining.ToString();
        }
        else if (state == RoundState.Fail || state == RoundState.Success)
        {
            // 성공/실패: 확인 버튼을 누른 인원 현황 표시
            _confirmedCountText.text = $"{RoundManager.Instance.ConfirmedCount}/{RoundManager.Instance.TotalPlayerCount}";
        }
    }

    private void HandleRoundStateChanged(RoundState state)
    {
        switch (state)
        {
            case RoundState.Round1Clear:
                _resultText.text = "1라운드 클리어";
                _confirmButton.gameObject.SetActive(false); // 15초 후 자동 2라운드 전환
                _nextRoundText.SetActive(true);
                ShowPanel();
                break;
            case RoundState.Success:
                _resultText.text = "검거 성공";
                _confirmButton.gameObject.SetActive(true);
                _nextRoundText.SetActive(false);
                ShowPanel();
                break;
            case RoundState.Fail:
                _resultText.text = "검거 실패";
                _confirmButton.gameObject.SetActive(true);
                _nextRoundText.SetActive(false);
                ShowPanel();
                break;
            default:
                _panel.SetActive(false);
                _inventoryCanvas.SetActive(true);
                _nextRoundText.SetActive(false);
                break;
        }
    }

    private void ShowPanel()
    {
        _panel.SetActive(true);
        _inventoryCanvas.SetActive(false);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private void HandleConfirmButtonClicked()
    {
        RoundManager.Instance.ConfirmResultServerRpc();
    }
}