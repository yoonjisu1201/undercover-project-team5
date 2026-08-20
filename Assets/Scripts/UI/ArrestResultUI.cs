using System;
using Cysharp.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 검거 판정 결과(범인/오검거) 패널을 표시한다.
public class ArrestResultUI : MonoBehaviour, IClosableUi
{
    private const float RevealedNoticeSeconds = 1.5f;

    [Header("=== 수갑 체결 연출 ===")]
    [SerializeField] private HandcuffArrestEffect _handcuffArrestEffect;

    private bool _isHandcuffEffectPlaying;

    [SerializeField] private GameObject _wrongTargetPanel;       //"범인이 아니었음"을 보여주는 패널
    [SerializeField] private TMP_Text _wrongTargetSubText;
    [SerializeField] private GameObject _arrestSuccessPanel;     //"범인이 맞았음"을 보여주는 패널
    [SerializeField] private GameObject _revealedPanel;          //"외계인이 본 모습을 드러냈습니다!" 안내 문구, 성공 판정 시 잠깐 표시

    [SerializeField] private ArrestCandidatePortrait _candidatePortrait; //검거 대상 NPC 실시간 이미지

    [Header("=== 검거 확인 패널 ===")]
    [SerializeField] private GameObject _confirmPanel;   //"외계인으로 지정하시겠습니까?" 확인 패널
    [SerializeField] private Button _confirmYesButton;
    [SerializeField] private Button _confirmNoButton;

    // 확인 패널에서 [예]를 눌렀을 때 검거를 요청할 대상.
    private ArrestCandidateInteractable _pendingCandidate;

    // 커서를 풀어준 상태인지. GameplayUiMode의 Activate/Deactivate를 정확히 짝 맞춰 호출하기 위해 기록해둔다.
    private bool _cursorActivated;

    private void Start()
    {
        _wrongTargetPanel.SetActive(false);
        _arrestSuccessPanel.SetActive(false);
        _revealedPanel.SetActive(false);
        _confirmPanel.SetActive(false);

        _confirmYesButton.onClick.AddListener(HandleConfirmYesClicked);
        _confirmNoButton.onClick.AddListener(HandleConfirmNoClicked);

        ArrestJudgementManager.Instance.OnJudged += HandleJudged;
        RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
    }

    private void OnDestroy()
    {
        _confirmYesButton.onClick.RemoveListener(HandleConfirmYesClicked);
        _confirmNoButton.onClick.RemoveListener(HandleConfirmNoClicked);

        if (ArrestJudgementManager.Instance != null)
        {
            ArrestJudgementManager.Instance.OnJudged -= HandleJudged;
        }

        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }

        if (_cursorActivated)
        {
            _cursorActivated = false;
            GameplayUiMode.Instance?.DeactivateCursor();
        }

        GameplayUiMode.Instance?.UnregisterUi(this);
    }

    // 다른 UI가 커서를 다시 잠그더라도 이 패널들이 열려 있는 동안은 커서가 보여야 한다.
    // GameplayUiMode의 카운터만 믿으면 짝이 어긋났을 때 커서가 잠긴 채로 남는다.
    private void LateUpdate()
    {
        if (!_confirmPanel.activeSelf && !_arrestSuccessPanel.activeSelf && !_wrongTargetPanel.activeSelf)
        {
            return;
        }

        if (!Cursor.visible || Cursor.lockState != CursorLockMode.None)
        {
            // 값만 다시 써도 화면에는 안 나오는 경우가 있어 Locked를 한 번 거친다.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // NPC에 E를 눌렀을 때 ArrestCandidateInteractable이 호출하는 진입점.
    // 바로 판정하지 않고 "외계인으로 지정하시겠습니까?" 확인 패널을 먼저 띄운다.
    public void RequestOpenConfirmPanel(ArrestCandidateInteractable candidate)
    {
        if (candidate == null || _confirmPanel.activeSelf) return;

        _pendingCandidate = candidate;
        _candidatePortrait.ShowCandidate(candidate.NetworkObject);
        _confirmPanel.SetActive(true);

        UpdateCursorState();
        GameplayUiMode.Instance?.RegisterUi(this);
    }

    // 시민 검거 상호작용이 완료됐을 때 로컬 플레이어 화면에만 수갑 연출을 재생한다.
    // 연출이 끝나면 서버에 검거 판정을 요청한다.
    public void RequestPlayHandcuffEffect(ArrestCandidateInteractable candidate)
    {
        if (candidate == null || _isHandcuffEffectPlaying)
        {
            candidate?.CancelPendingConfirmation();
            return;
        }

        PlayHandcuffEffectAsync(candidate).Forget();
    }

    private async UniTaskVoid PlayHandcuffEffectAsync(ArrestCandidateInteractable candidate)
    {
        _isHandcuffEffectPlaying = true;

        if (_handcuffArrestEffect == null)
        {
            Debug.LogError("[ArrestResultUI] 수갑 체결 연출 참조가 없습니다.", this);
        }
        else
        {
            await _handcuffArrestEffect.PlayAsync();
        }

        candidate.ConfirmArrest();
        _isHandcuffEffectPlaying = false;
    }

    private void HandleConfirmYesClicked()
    {
        ArrestCandidateInteractable candidate = _pendingCandidate;
        CloseConfirmPanel(false);
        candidate?.ConfirmArrest();
    }

    private void HandleConfirmNoClicked()
    {
        CloseConfirmPanel(true);
    }

    // resumeCandidate가 true면 붙잡아 둔 NPC를 다시 움직이게 한다.
    // [예]로 닫을 때는 판정 흐름이 이어지므로 풀지 않는다.
    private void CloseConfirmPanel(bool resumeCandidate)
    {
        if (!_confirmPanel.activeSelf) return;

        _confirmPanel.SetActive(false);

        if (resumeCandidate)
        {
            _pendingCandidate?.CancelPendingConfirmation();
            // 취소했는데 캡처해둔 초상이 남아 있으면 다음에 열 때 이전 대상이 잠깐 보인다.
            _candidatePortrait.Clear();
        }

        _pendingCandidate = null;
        GameplayUiMode.Instance?.UnregisterUi(this);
        UpdateCursorState();
    }

    // 검거 판정 결과가 나오면 결과 패널을 띄운다. 범인이 아니어도(오검거) 패널은 표시한다.
    private void HandleJudged(ArrestResult result, NetworkObject candidate)
    {
        if (candidate != null)
        {
            _candidatePortrait.ShowCandidate(candidate);
        }

        if (result == ArrestResult.WrongTarget)
        {
            _wrongTargetSubText.text =
                $"[{ArrestJudgementManager.Instance.LastArrestingPlayerName}] 님이 범인이 아닌\n" +
                "시민을 검거하려고 했습니다!";
        }

        _arrestSuccessPanel.SetActive(result == ArrestResult.Success);
        _wrongTargetPanel.SetActive(result == ArrestResult.WrongTarget);

        // 판정 결과 패널은 한 번 뜨면 Close()(ESC 또는 닫기 버튼)를 눌러야만 닫힌다. 시간 제한을 없애기 위함.
        UpdateCursorState();
        GameplayUiMode.Instance?.RegisterUi(this);

        if (result == ArrestResult.Success)
        {
            ShowRevealedNoticeAsync().Forget();
        }
    }

    // ESC(스택)로 닫을 때 열려 있는 창을 로컬에서 감춘다.
    public void Close()
    {
        CloseConfirmPanel(true);
        CloseResultPanel();
    }

    // 판정 결과창은 자동으로 닫히지 않으므로 로컬에서 직접 닫는다. (서버 상태는 그대로)
    private void CloseResultPanel()
    {
        if (!_arrestSuccessPanel.activeSelf && !_wrongTargetPanel.activeSelf) return;

        _arrestSuccessPanel.SetActive(false);
        _wrongTargetPanel.SetActive(false);
        GameplayUiMode.Instance?.UnregisterUi(this);
        UpdateCursorState();
    }

    // 라운드가 끝나면 라운드 결과 패널이 뜨므로, 안 닫고 남겨둔 판정 결과창을 대신 정리한다.
    private void HandleRoundStateChanged(RoundState state)
    {
        if (state == RoundState.InRound) return;

        CloseConfirmPanel(true);
        CloseResultPanel();
    }

    // 성공 판정(진짜 범인 검거) 순간, "외계인이 본 모습을 드러냈습니다!" 안내를 잠깐 띄운다.
    private async UniTaskVoid ShowRevealedNoticeAsync()
    {
        _revealedPanel.SetActive(true);
        await UniTask.Delay(TimeSpan.FromSeconds(RevealedNoticeSeconds), cancellationToken: this.GetCancellationTokenOnDestroy());
        _revealedPanel.SetActive(false);
    }

    // 판정 결과 패널이 열려 있으면 커서를 풀어준다.
    private void UpdateCursorState()
    {
        bool anyPanelOpen = _arrestSuccessPanel.activeSelf || _wrongTargetPanel.activeSelf || _confirmPanel.activeSelf;

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
}
