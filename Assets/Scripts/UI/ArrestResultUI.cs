using System;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

// 검거 판정 결과(범인/오검거) 패널을 표시한다.
public class ArrestResultUI : MonoBehaviour, IClosableUi
{
    private const float RevealedNoticeSeconds = 1.5f;

    [SerializeField] private GameObject _wrongTargetPanel;       //"범인이 아니었음"을 보여주는 패널
    [SerializeField] private GameObject _arrestSuccessPanel;     //"범인이 맞았음"을 보여주는 패널
    [SerializeField] private GameObject _revealedPanel;          //"외계인이 본 모습을 드러냈습니다!" 안내 문구, 성공 판정 시 잠깐 표시

    [SerializeField] private ArrestCandidatePortrait _candidatePortrait; //검거 대상 NPC 실시간 이미지

    // 커서를 풀어준 상태인지. GameplayUiMode의 Activate/Deactivate를 정확히 짝 맞춰 호출하기 위해 기록해둔다.
    private bool _cursorActivated;

    private void Start()
    {
        _wrongTargetPanel.SetActive(false);
        _arrestSuccessPanel.SetActive(false);
        _revealedPanel.SetActive(false);

        ArrestJudgementManager.Instance.OnJudged += HandleJudged;
        RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
    }

    private void OnDestroy()
    {
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

    // 검거 판정 결과가 나오면 결과 패널을 띄운다. 범인이 아니어도(오검거) 패널은 표시한다.
    private void HandleJudged(ArrestResult result, NetworkObject candidate)
    {
        if (candidate != null)
        {
            _candidatePortrait.ShowCandidate(candidate);
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

    // ESC(스택)로 닫을 때 판정 결과창을 로컬에서 감춘다.
    public void Close()
    {
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
        bool anyPanelOpen = _arrestSuccessPanel.activeSelf || _wrongTargetPanel.activeSelf;

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
