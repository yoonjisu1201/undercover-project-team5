using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ArrestVoteUI : MonoBehaviour, IClosableUi
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

    [SerializeField] private GameObject _wrongTargetPanel;       //Judged 상태에서 "범인이 아니었음"을 보여주는 패널
    [SerializeField] private GameObject _arrestSuccessPanel;     //Judged 상태에서 "범인이 맞았음"을 보여주는 패널

    [SerializeField] private ArrestCandidatePortrait _candidatePortrait; //검거 후보 NPC 실시간 이미지
    // 커서를 풀어준 상태인지. GameplayUiMode의 Activate/Deactivate를 정확히 짝 맞춰 호출하기 위해 기록해둔다.
    private bool _cursorActivated;

    // 이번 투표 사이클에서 후보 이미지를 이미 캡처했는지. NetworkVariable 동기화 순서에 상관없이 한 번만 캡처하기 위함.
    private bool _hasCapturedForCurrentVote;

    // 확인 패널이 열려있는 동안 어떤 NPC를 검거 후보로 요청할지 기억해둔다.
    private ArrestCandidateInteractable _pendingCandidate;

    private void Start()
    {
        _startVotePanel.SetActive(false);
        _voteExhaustedPanel.SetActive(false);
        _voteAreaPassedPanel.SetActive(false);
        _voteAreaRejectedPanel.SetActive(false);
        _resultCountdownText.gameObject.SetActive(false);
        _wrongTargetPanel.SetActive(false);
        _arrestSuccessPanel.SetActive(false);

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

        // 상태 변경 콜백 시점엔 ArrestCandidate가 아직 동기화 전일 수 있어서, 매 프레임 조건이 갖춰졌는지 확인해서 캡처한다.
        if (ArrestVoteManager.Instance.CurrentVoteState == ArrestVoteState.Voting
            && !_hasCapturedForCurrentVote
            && ArrestVoteManager.Instance.ArrestCandidate != null)
        {
            _candidatePortrait.ShowCandidate(ArrestVoteManager.Instance.ArrestCandidate);
            _hasCapturedForCurrentVote = true;
        }

        switch (ArrestVoteManager.Instance.CurrentVoteState)
        {
            case ArrestVoteState.Voting:
                // 투표 중일 때만 남은 시간을 갱신한다 (RoundTimerDisplay와 동일한 패턴)
                _remainingTimeText.text = Mathf.CeilToInt(ArrestVoteManager.Instance.GetRemainingVoteTime()).ToString();
                break;
            case ArrestVoteState.Passed:
            case ArrestVoteState.Judged:
            case ArrestVoteState.Rejected:
                // 결과 화면이 Idle로 돌아가기까지 남은 시간을 갱신한다
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

        GameplayUiMode.Instance?.UnregisterUi(this);
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
        GameplayUiMode.Instance?.RegisterUi(this);   // ESC로 '아니요' 처리 가능하게 등록

        // 확인 패널이 뜨는 시점에 캡처해서 투표 화면까지 이어서 쓴다.
        _candidatePortrait.ShowCandidate(candidate.NetworkObject);
    }

    // 확인 패널에서 [네]를 누르면 검거 후보를 고정한 뒤 서버에 투표 시작을 요청하고 확인 패널을 닫는다.
    private void ConfirmStartYes()
    {
        _pendingCandidate?.ConfirmArrestCandidate();
        _pendingCandidate = null;

        _startVotePanel.SetActive(false);
        UpdateCursorState();
        GameplayUiMode.Instance?.UnregisterUi(this);
    }

    // 확인 패널에서 [아니요]를 누르면 멈춰뒀던 NPC 이동을 재개하고 패널만 닫는다.
    private void ConfirmStartNo()
    {
        _pendingCandidate?.CancelPendingConfirmation();
        _pendingCandidate = null;
        _startVotePanel.SetActive(false);
        UpdateCursorState();
        GameplayUiMode.Instance?.UnregisterUi(this);

        _candidatePortrait.Clear(); // 투표를 시작하지 않았으니 캡처해둔 이미지삭제
    }

    // ESC(스택)로 닫을 때: 지정 확인창이면 '아니요'와 동일, 판정 결과창이면 로컬에서 결과창만 감춘다.
    public void Close()
    {
        if (_startVotePanel.activeSelf)
        {
            ConfirmStartNo();   // #4: ESC = 아니요
            return;
        }

        // #6: 판정 결과창(시민/외계인)은 자동 카운트다운보다 빨리 로컬에서 닫는다. (서버 상태는 그대로)
        if (_arrestSuccessPanel.activeSelf || _wrongTargetPanel.activeSelf)
        {
            _arrestSuccessPanel.SetActive(false);
            _wrongTargetPanel.SetActive(false);
            _resultCountdownText.gameObject.SetActive(false);
            _votePanel.SetActive(false);
            GameplayUiMode.Instance?.UnregisterUi(this);
            UpdateCursorState();
        }
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
            || state == ArrestVoteState.Judged
            || state == ArrestVoteState.Rejected;
        _votePanel.SetActive(panelVisible);
        UpdateCursorState();

        if (state != ArrestVoteState.Voting)
        {
            _hasCapturedForCurrentVote = false; // 다음 투표를 위해 리셋
        }

        // 투표 중일 때만 O/X 버튼 영역을 보여주고, 가결/부결 결과 패널로 그 영역을 가린다.
        _voteAreaPanel.SetActive(state == ArrestVoteState.Voting);
        _voteAreaPassedPanel.SetActive(state == ArrestVoteState.Passed);
        _voteAreaRejectedPanel.SetActive(state == ArrestVoteState.Rejected);

        // Judged 상태(가결 판정 결과 표시 중)에서만, 범인 판정 결과에 맞는 패널을 보여준다.
        // 이 단계에서는 NPC 이동을 멈추는 로직이 없으므로 NPC는 계속 움직인다.
        bool isJudgedSuccess = state == ArrestVoteState.Judged
            && ArrestJudgementManager.Instance != null
            && ArrestJudgementManager.Instance.CurrentArrestResult == ArrestResult.Success;
        bool isJudgedWrongTarget = state == ArrestVoteState.Judged
            && ArrestJudgementManager.Instance != null
            && ArrestJudgementManager.Instance.CurrentArrestResult == ArrestResult.WrongTarget;
        _arrestSuccessPanel.SetActive(isJudgedSuccess);
        _wrongTargetPanel.SetActive(isJudgedWrongTarget);

        // 투표 결과(가결/판정/부결) 화면일 때만 카운트다운 텍스트를 보여준다.
        _resultCountdownText.gameObject.SetActive(state == ArrestVoteState.Passed
            || state == ArrestVoteState.Judged
            || state == ArrestVoteState.Rejected);

        // 새 투표가 시작될 때마다 O/X 버튼을 다시 눌러진 상태로 되돌린다.
        if (state == ArrestVoteState.Voting)
        {
            _yesButton.interactable = true;
            _noButton.interactable = true;
        }

        // 판정 결과창(시민/외계인)일 때만 ESC로 닫을 수 있게 스택에 등록한다.
        if (state == ArrestVoteState.Judged)
        {
            GameplayUiMode.Instance?.RegisterUi(this);
        }
        else if (!_startVotePanel.activeSelf)   // 지정 확인창이 열려있는 경우는 건드리지 않는다
        {
            GameplayUiMode.Instance?.UnregisterUi(this);
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
