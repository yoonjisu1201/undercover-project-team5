using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;

// 모든 미션 UI의 종료와 완료 확인을 공통 처리한다.
public sealed class MissionUIController : MonoBehaviour, IClosableUi
{
    [Header("결과 문구 (비워 두면 프리팹에 적힌 문구를 그대로 쓴다)")]
    [SerializeField] private LocalizedString _resultTitle;

    [Tooltip("{0} 에 기계 번호가 들어간다.")]
    [SerializeField] private LocalizedString _resultMessage;

    private MissionInteractable _owner;
    private TMP_Text _timerText;
    private bool _completionReady;
    private bool _isClosing;

    private void OnEnable()
    {
        // 열림음은 MissionInteractable이 Mission_UiOpen으로 낸다.
        GameplayUiMode.Instance?.RegisterUi(this, playOpenSound: false);
    }

    private void OnDisable()
    {
        GameplayUiMode.Instance?.UnregisterUi(this);
    }

    // ESC(스택)로 닫을 땐 '취소'로 처리한다. (버튼의 Close()는 완료 처리라 구분)
    void IClosableUi.Close()
    {
        CloseWithoutCompletion();
    }

    // 프리팹에 있는 공통 타이머 텍스트를 찾아둔다.
    private void Awake()
    {
        Transform timer = FindChild("Timer");
        if (timer != null)
        {
            _timerText = timer.GetComponent<TMP_Text>();
        }

    }

    [Tooltip("{0} 에 mm:ss 가 들어간다.")]
    [SerializeField] private LocalizedString _timeLeftFormat;

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
        _timerText.text = _timeLeftFormat.GetLocalizedString($"{minutes:00}:{seconds:00}");
    }

    // UI를 연 월드 미션 기계를 연결한다.
    public void Initialize(MissionInteractable owner)
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

        ApplyResultText("ResultTitle", _resultTitle);
        ApplyResultText("ResultMessage", _resultMessage);

        overlay.gameObject.SetActive(true);
    }

    // 미션마다 결과 문구가 다르므로, 키가 지정된 경우에만 프리팹 문구를 덮어쓴다.
    // UI 프리팹은 여러 기계가 공유하므로, 기계별로 다른 번호는 {0} 자리에 채워 넣는다.
    private void ApplyResultText(string childName, LocalizedString localized)
    {
        if (localized == null || localized.IsEmpty)
        {
            return;
        }

        string text = _owner != null
            ? localized.GetLocalizedString(_owner.TargetNumber)
            : localized.GetLocalizedString();

        Transform target = FindChild(childName);
        if (target != null && target.TryGetComponent(out TMP_Text label))
        {
            label.text = text;
        }
    }

    // 결과 확인 또는 뒤로 가기 버튼에서 UI를 닫는다.
    public void Close()
    {
        CloseInternal(submitCompletion: true);
    }

    // Unity Button의 On Click 이벤트에서 결과 화면 또는 미션 UI를 닫는다.
    public void OnCloseButtonClick()
    {
        Close();
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

        // 단계형 미션은 UI를 보관해 다시 열었을 때 완료한 단계부터 이어간다.
        bool shouldPreserveProgress = TryGetComponent<CCTVSignalRepairGame>(out _);
        if (submitCompletion && !_completionReady && shouldPreserveProgress)
        {
            gameObject.SetActive(false);
            _owner?.NotifyUISuspended(this);
            _isClosing = false;
            return;
        }

        _owner?.NotifyUIClosed(this);
        Destroy(gameObject);
    }

    // 비활성 오브젝트를 포함해 이름이 같은 자식을 찾는다.
    private Transform FindChild(string childName)
    {
        return GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(child => child.name == childName);
    }
}
