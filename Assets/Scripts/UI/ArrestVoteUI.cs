using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ArrestVoteUI : MonoBehaviour
{
    [SerializeField] private TMP_Text _remainingVoteText;  //남은 검거 투표 횟수
    [SerializeField] private TMP_Text _votePanelRemainingVoteText;  //VotePannel 내부에 남은 투표 횟수를 추가로 표시할 텍스트 (선택)
    [SerializeField] private TMP_Text _remainingTimeText;  //투표 남은 시간
    [SerializeField] private TMP_Text _submittedCountText;  //제출 완료 인원
    [SerializeField] private Button _yesButton;  //O 버튼
    [SerializeField] private Button _noButton;   //X 버튼
    [SerializeField] private GameObject _votePanel;  //투표 중일 때만 보여줄 패널

    [SerializeField] private GameObject _startVotePanel;  //용의자npc 상호작용시 노출되는 패널
    [SerializeField] private Button _startYesButton;  //검거투표 시작 [네] 버튼
    [SerializeField] private Button _startNoButton;   //검거투표 시작 [아니요] 버튼

    [SerializeField] private GameObject _voteExhaustedPanel;  //"검거 투표 횟수가 소진되었습니다" 안내 패널
    private const float VoteExhaustedNoticeSeconds = 3f;
    private CancellationTokenSource _voteExhaustedCts;

    [SerializeField] private GameObject _voteAreaPanel;          //O/X 투표 버튼이 있는 패널 (Generated_VoteArea)
    [SerializeField] private GameObject _voteAreaPassedPanel;    //가결 결과 패널 (Generated_VoteArea를 가리고 표시)
    [SerializeField] private GameObject _voteAreaRejectedPanel;  //부결 결과 패널 (Generated_VoteArea를 가리고 표시)
    [SerializeField] private TMP_Text _resultCountdownText;      //투표 결과 화면에서 Idle로 돌아가기까지 남은 시간(3,2,1) 표시

    // 커서를 풀어준 상태인지. GameplayUiMode의 Activate/Deactivate를 정확히 짝 맞춰 호출하기 위해 기록해둔다.
    private bool _cursorActivated;

    // 확인 패널이 열려있는 동안 어떤 NPC를 검거 후보로 요청할지 기억해둔다.
    private ArrestCandidateInteractable _pendingCandidate;

    private void Start()
    {
        _startVotePanel.SetActive(false);
        _voteExhaustedPanel.SetActive(false);
        _voteAreaPassedPanel.SetActive(false);
        _voteAreaRejectedPanel.SetActive(false);

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
        _startYesButton.onClick.AddListener(ConfirmStartYes);
        _startNoButton.onClick.AddListener(ConfirmStartNo);
    }

    private void Update()
    {
        if (ArrestVoteManager.Instance == null) return;

        switch (ArrestVoteManager.Instance.CurrentVoteState)
        {
            case ArrestVoteState.Voting:
                // 투표 중일 때만 남은 시간을 갱신한다 (RoundTimerDisplay와 동일한 패턴)
                _remainingTimeText.text = Mathf.CeilToInt(ArrestVoteManager.Instance.GetRemainingVoteTime()).ToString();
                break;
            case ArrestVoteState.Passed:
            case ArrestVoteState.Rejected:
                // 결과 화면이 Idle로 돌아가기까지 남은 시간을 3, 2, 1 형태로 갱신한다
                _resultCountdownText.text = Mathf.CeilToInt(ArrestVoteManager.Instance.GetRemainingResultTime()).ToString();
                break;
        }
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
        _startYesButton.onClick.RemoveListener(ConfirmStartYes);
        _startNoButton.onClick.RemoveListener(ConfirmStartNo);

        if (_cursorActivated)
        {
            _cursorActivated = false;
            GameplayUiMode.Instance?.DeactivateCursor();
        }
    }

    // NPC 상호작용(ArrestCandidateInteractable)에서 검거 후보 지정 시 호출. 이미 투표 중이거나 횟수가 소진됐으면 패널 대신 안내만 띄운다.
    public void RequestOpenStartVotePanel(ArrestCandidateInteractable candidate)
    {
        if (ArrestVoteManager.Instance == null) return;
        if (ArrestVoteManager.Instance.CurrentVoteState != ArrestVoteState.Idle) return;
        if (_startVotePanel.activeSelf) return;

        if (ArrestVoteManager.Instance.RemainingVoteAttempts <= 0)
        {
            ShowVoteExhaustedNotice();
            return;
        }

        _pendingCandidate = candidate;
        _startVotePanel.SetActive(true);
        UpdateCursorState();
    }

    // 확인 패널에서 [네]를 누르면 검거 후보를 고정한 뒤 서버에 투표 시작을 요청하고 확인 패널을 닫는다.
    private void ConfirmStartYes()
    {
        _pendingCandidate?.ConfirmArrestCandidate();
        _pendingCandidate = null;

        ArrestVoteManager.Instance.RequestStartVoteServerRpc();
        _startVotePanel.SetActive(false);
        UpdateCursorState();
    }

    // 확인 패널에서 [아니요]를 누르면 멈춰뒀던 NPC 이동을 재개하고 패널만 닫는다.
    private void ConfirmStartNo()
    {
        _pendingCandidate?.CancelPendingConfirmation();
        _pendingCandidate = null;
        _startVotePanel.SetActive(false);
        UpdateCursorState();
    }

    // 이번 라운드 검거 투표 횟수가 소진됐을 때 안내 문구를 2초간 띄운다.
    private void ShowVoteExhaustedNotice()
    {
        _voteExhaustedCts?.Cancel();
        _voteExhaustedCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

        _voteExhaustedPanel.SetActive(true);
        HideVoteExhaustedNoticeAfterDelayAsync(_voteExhaustedCts.Token).Forget();
    }

    private async UniTaskVoid HideVoteExhaustedNoticeAfterDelayAsync(CancellationToken cancellationToken)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(VoteExhaustedNoticeSeconds), cancellationToken: cancellationToken);
        _voteExhaustedPanel.SetActive(false);
    }

    // 투표 패널/확인 패널 중 하나라도 열려 있으면 커서를 풀어준다.
    private void UpdateCursorState()
    {
        bool anyPanelOpen = _votePanel.activeSelf || _startVotePanel.activeSelf;

        if (anyPanelOpen && !_cursorActivated)
        {
            _cursorActivated = true;
            GameplayUiMode.Instance?.ActivateCursor();
        }
        else if (!anyPanelOpen && _cursorActivated)
        {
            _cursorActivated = false;
            GameplayUiMode.Instance?.DeactivateCursor();
        }
    }

    private void HandleRemainingVoteAttemptsChanged(int remaining)
    {
        _remainingVoteText.text = remaining.ToString();

        if (_votePanelRemainingVoteText != null)
        {
            _votePanelRemainingVoteText.text = remaining.ToString();
        }
    }

    private void HandleSubmittedCountChanged(int submitted)
    {
        _submittedCountText.text = $"{submitted}/{ArrestVoteManager.Instance.VoteParticipantCount}";
    }

    // 투표 중이거나 결과를 보여주는 동안 패널을 보여주고 커서를 풀어서 버튼을 클릭할 수 있게 한다.
    private void HandleVoteStateChanged(ArrestVoteState state)
    {
        bool panelVisible = state == ArrestVoteState.Voting
            || state == ArrestVoteState.Passed
            || state == ArrestVoteState.Rejected;
        _votePanel.SetActive(panelVisible);
        UpdateCursorState();

        // 투표 중일 때만 O/X 버튼 영역을 보여주고, 가결/부결 결과 패널로 그 영역을 가린다.
        _voteAreaPanel.SetActive(state == ArrestVoteState.Voting);
        _voteAreaPassedPanel.SetActive(state == ArrestVoteState.Passed);
        _voteAreaRejectedPanel.SetActive(state == ArrestVoteState.Rejected);

        // 새 투표가 시작될 때마다 O/X 버튼을 다시 눌러진 상태로 되돌린다.
        if (state == ArrestVoteState.Voting)
        {
            _yesButton.interactable = true;
            _noButton.interactable = true;
        }
    }

    // O/X 버튼 리스너로 등록되는 콜백. 한 번 선택하면 두 버튼 모두 비활성화해서 중복 제출을 막는다.
    private void VoteYes()
    {
        ArrestVoteManager.Instance.SubmitVoteServerRpc(true);
        _yesButton.interactable = false;
        _noButton.interactable = false;
    }

    private void VoteNo()
    {
        ArrestVoteManager.Instance.SubmitVoteServerRpc(false);
        _yesButton.interactable = false;
        _noButton.interactable = false;
    }
}
