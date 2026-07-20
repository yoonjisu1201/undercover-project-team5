using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ArrestVoteUI : MonoBehaviour
{
    [SerializeField] private TMP_Text _remainingVoteText;  //남은 검거 투표 횟수
    [SerializeField] private TMP_Text _remainingTimeText;  //투표 남은 시간
    [SerializeField] private TMP_Text _submittedCountText;  //제출 완료 인원
    [SerializeField] private Button _yesButton;  //O 버튼
    [SerializeField] private Button _noButton;   //X 버튼
    [SerializeField] private GameObject _votePanel;  //투표 중일 때만 보여줄 패널

    // 투표 중 커서를 풀어준 상태인지. GameplayUiMode의 Activate/Deactivate를 정확히 짝 맞춰 호출하기 위해 기록해둔다.
    private bool _cursorActivated;

    private void Start()
    {
        //검거 횟수가 변경될때마다 UI 변경하기 위한 이벤트 구독
        ArrestVoteManager.Instance.OnRemainingVoteAttemptsChanged += HandleRemainingVoteAttemptsChanged;
        ArrestVoteManager.Instance.OnVoteStateChanged += HandleVoteStateChanged;
        ArrestVoteManager.Instance.OnSubmittedCountChanged += HandleSubmittedCountChanged;

        // 구독 전에 이미 값이 세팅돼 있을 수 있으므로 현재 값 즉시 반영
        HandleRemainingVoteAttemptsChanged(ArrestVoteManager.Instance.RemainingVoteAttempts);
        HandleVoteStateChanged(ArrestVoteManager.Instance.CurrentVoteState);
        HandleSubmittedCountChanged(ArrestVoteManager.Instance.SubmittedCount);

        _yesButton.onClick.AddListener(VoteYes);
        _noButton.onClick.AddListener(VoteNo);
    }

    private void Update()
    {
        // 투표 중일 때만 남은 시간을 갱신한다 (RoundTimerDisplay와 동일한 패턴)
        if (ArrestVoteManager.Instance == null) return;
        if (ArrestVoteManager.Instance.CurrentVoteState != ArrestVoteState.Voting) return;

        _remainingTimeText.text = Mathf.CeilToInt(ArrestVoteManager.Instance.GetRemainingVoteTime()).ToString();
    }

    private void OnDestroy()
    {
        if (ArrestVoteManager.Instance != null)
        {
            ArrestVoteManager.Instance.OnRemainingVoteAttemptsChanged -= HandleRemainingVoteAttemptsChanged;
            ArrestVoteManager.Instance.OnVoteStateChanged -= HandleVoteStateChanged;
            ArrestVoteManager.Instance.OnSubmittedCountChanged -= HandleSubmittedCountChanged;
        }

        _yesButton.onClick.RemoveListener(VoteYes);
        _noButton.onClick.RemoveListener(VoteNo);

        if (_cursorActivated)
        {
            _cursorActivated = false;
            GameplayUiMode.Instance?.DeactivateCursor();
        }
    }

    private void HandleRemainingVoteAttemptsChanged(int remaining)
    {
        _remainingVoteText.text = remaining.ToString();
    }

    private void HandleSubmittedCountChanged(int submitted)
    {
        _submittedCountText.text = $"{submitted}/{ArrestVoteManager.Instance.VoteParticipantCount}";
    }

    // 투표 중일 때만 패널을 보여주고 커서를 풀어서 O/X 버튼을 클릭할 수 있게 한다.
    private void HandleVoteStateChanged(ArrestVoteState state)
    {
        _votePanel.SetActive(state == ArrestVoteState.Voting);

        if (state == ArrestVoteState.Voting && !_cursorActivated)
        {
            _cursorActivated = true;
            GameplayUiMode.Instance?.ActivateCursor();
        }
        else if (state != ArrestVoteState.Voting && _cursorActivated)
        {
            _cursorActivated = false;
            GameplayUiMode.Instance?.DeactivateCursor();
        }
    }

    // O/X 버튼 리스너로 등록되는 콜백
    private void VoteYes()
    {
        ArrestVoteManager.Instance.SubmitVoteServerRpc(true);
    }

    private void VoteNo()
    {
        ArrestVoteManager.Instance.SubmitVoteServerRpc(false);
    }
}
