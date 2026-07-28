using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 모든 미니게임 UI의 종료와 완료 확인을 공통 처리한다.
public sealed class MiniGameUIController : MonoBehaviour
{
    private MiniGameInteractable _owner;
    private TMP_Text _timerText;
    private bool _completionReady;
    private bool _isClosing;

    // 프리팹에 있는 공통 타이머 텍스트를 찾아둔다.
    private void Awake()
    {
        Transform timer = FindChild("Timer");
        if (timer != null)
        {
            _timerText = timer.GetComponent<TMP_Text>();
        }

        Transform closeResultButton = FindChild("CloseResultButton");
        if (closeResultButton != null && closeResultButton.TryGetComponent(out Button button))
        {
            button.onClick.AddListener(Close);
        }
    }

    // 네트워크 서버 시간으로 계산된 현재 라운드의 남은 시간을 표시한다.
    private void Update()
    {
        if (_timerText == null || RoundManager.Instance == null)
        {
            return;
        }

        float remaining = RoundManager.Instance.GetRemainingTime();
        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);
        _timerText.text = $"TIME  {minutes:00}:{seconds:00}";
    }

    // UI를 연 월드 미니게임 기계를 연결한다.
    public void Initialize(MiniGameInteractable owner)
    {
        _owner = owner;
    }

    // 게임 로직이 성공했음을 기록해 결과 확인 시 서버 완료 요청을 보내도록 한다.
    public void MarkCompletionReady()
    {
        _completionReady = true;
    }

    // 이미 완료된 게임은 퍼즐 대신 완료 안내 결과 창만 표시한다.
    public void ShowCompletedState()
    {
        Transform overlay = FindChild("ResultOverlay");
        if (overlay == null)
        {
            Debug.LogError($"'{name}'에 ResultOverlay가 없습니다.", this);
            return;
        }

        SetText("ResultTitle", "완료된 게임");
        SetText("ResultMessage", "이미 완료되어 다시 플레이할 수 없습니다.");
        overlay.gameObject.SetActive(true);
    }

    // 결과 확인 또는 뒤로 가기 버튼에서 UI를 닫는다.
    public void Close()
    {
        CloseInternal(submitCompletion: true);
    }

    // 라운드 변경으로 닫힐 때는 이전 퍼즐의 완료 요청을 보내지 않는다.
    public void CloseWithoutCompletion()
    {
        CloseInternal(submitCompletion: false);
    }

    // 완료 요청 여부를 구분해 UI와 입력 상태를 정리한다.
    private void CloseInternal(bool submitCompletion)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        if (submitCompletion && _completionReady)
        {
            _owner?.RequestCompletion();
        }

        GameplayUiMode.Instance?.DeactivateCursor();
        _owner?.NotifyUIClosed(this);
        Destroy(gameObject);
    }

    // 이름으로 찾은 자식 Text의 문구를 변경한다.
    private void SetText(string childName, string value)
    {
        Transform child = FindChild(childName);
        if (child != null && child.TryGetComponent(out TMP_Text text))
        {
            text.text = value;
        }
    }

    // 비활성 오브젝트를 포함해 이름이 같은 자식을 찾는다.
    private Transform FindChild(string childName)
    {
        return GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(child => child.name == childName);
    }
}
