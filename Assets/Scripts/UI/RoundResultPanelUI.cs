using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Serialization;
using UnityEngine.UI;

// 라운드 결과(라운드 클리어/성공/실패)를 표시하고, 방장이 확인 버튼을 누르면 대기방으로 돌아가도록 서버에 알린다.
// 방장이 아닌 인원에게는 버튼 대신 안내 문구를 띄우고, 아무도 누르지 않아도 카운트다운이 끝나면 서버가 자동으로 되돌린다.
public class RoundResultPanelUI : MonoBehaviour
{
    [SerializeField] private GameObject _panel;
    [Header("현지화 문구")]
    [Tooltip("{0} 에 라운드 번호가 들어간다.")]
    [SerializeField] private LocalizedString _roundClearedFormat;

    [SerializeField] private LocalizedString _arrestSuccess;
    [SerializeField] private LocalizedString _arrestFailed;

    [SerializeField] private TMP_Text _resultText;
    [SerializeField] private Button _confirmButton;
    [SerializeField] private GameObject _inventoryCanvas;
    [SerializeField] private TMP_Text _countdownText;
    [SerializeField] private GameObject _nextRoundText;
    [SerializeField] private GameObject _returnNoticeText; // 방장이 아닌 인원에게 보여줄 "잠시 후 대기방으로 이동합니다" 문구

    // 전원 확인 방식이던 시절의 "확인한 인원/총 인원" 자리를 대기방 복귀 카운트다운 표시로 그대로 재사용한다.
    [FormerlySerializedAs("_confirmedCountText")]
    [SerializeField] private TMP_Text _returnCountdownText;
    [SerializeField] private TMP_Text _remainingTimeText; // 라운드 종료 시점 남은 시간 표시용
    [SerializeField] private TMP_Text _wrongArrestCountText; // 해당 라운드의 오검거 횟수 표시용
    [SerializeField] private TMP_Text _clearRewardText; // 이번 라운드 획득 보상 표시용
    [SerializeField] private TMP_Text _totalCreditsText; // 보상 지급 후 누적 크레딧 표시용
    [SerializeField] private GameObject _creditSection; // 보상/누적 크레딧 표기 묶음 (라운드 클리어에서만 노출)
    [SerializeField] private ArrestCandidatePortrait _criminalPortrait; // 검거 투표 때 쓰는 것을 그대로 재사용
    [SerializeField] private CriminalNpcManager _criminalNpcManager; // static Instance가 없어 인스펙터에서 직접 연결
    [SerializeField] private ClueModulePreview _clueModulePreview; // 촬영된 단서 이미지 참조용, static Instance가 없어 인스펙터에서 직접 연결
    [SerializeField] private RawImage[] _clueImages; // 무작위로 뽑은 단서 이미지를 표시할 슬롯

    // #664: 상점 제거 전까지 라운드 보상(크레딧) 표기를 숨긴다. 되돌릴 때는 이 값을 켜면 된다.
    [SerializeField] private bool _showCreditSection;

    private float _roundRemainingTimeAtClearLocal;
    private float _roundClearCountdownEndTimeLocal;
    private float _resultReturnCountdownEndTimeLocal;

    private void Start()
    {
        _confirmButton.onClick.AddListener(HandleConfirmButtonClicked);
        RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
        RoundManager.Instance.OnRoundClearAnnounced += HandleRoundClearAnnounced;
        RoundManager.Instance.OnResultReturnCountdownAnnounced += HandleResultReturnCountdownAnnounced;

        HandleRoundStateChanged(RoundManager.Instance.CurrentState);
    }

    private void OnDestroy()
    {
        _confirmButton.onClick.RemoveListener(HandleConfirmButtonClicked);

        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
            RoundManager.Instance.OnRoundClearAnnounced -= HandleRoundClearAnnounced;
            RoundManager.Instance.OnResultReturnCountdownAnnounced -= HandleResultReturnCountdownAnnounced;
        }
    }

    // RPC로 전달받은 라운드 클리어 시점 값을 로컬에 저장한다. 서버 NetworkVariable을 직접 읽지 않아
    // 다른 NetworkVariable과의 갱신 순서 문제에서 자유롭다.
    private void HandleRoundClearAnnounced(float remainingTimeAtClear, float countdownDuration, int clearReward, int totalCredits)
    {
        _roundRemainingTimeAtClearLocal = remainingTimeAtClear;
        _roundClearCountdownEndTimeLocal = Time.time + countdownDuration;
        _clearRewardText.text = $"+ {clearReward:N0}";
        _totalCreditsText.text = totalCredits.ToString("N0");
    }

    // 성공/실패 결과창의 대기방 자동 복귀 카운트다운도 라운드 클리어와 같은 방식으로 로컬에 저장한다.
    private void HandleResultReturnCountdownAnnounced(float countdownDuration)
    {
        _resultReturnCountdownEndTimeLocal = Time.time + countdownDuration;
    }

    private void Update()
    {
        if (RoundManager.Instance == null) return;

        var state = RoundManager.Instance.CurrentState;

        if (state == RoundState.RoundClear)
        {
            // 라운드 클리어: 다음 라운드 자동 시작까지 남은 시간 표시
            int remaining = Mathf.CeilToInt(Mathf.Max(0f, _roundClearCountdownEndTimeLocal - Time.time));
            _countdownText.text = remaining.ToString();
            // RPC 도착 순서가 네트워크 상황에 따라 달라질 수 있어, 상태변경 시 한 번만 읽지 않고 매 프레임 갱신해 자체 교정한다.
            ShowRoundResultStats(state);
        }
        else if (state == RoundState.Fail || state == RoundState.Success)
        {
            // 성공/실패: 대기방 자동 복귀까지 남은 시간 표시 (방장/비방장 모두 같은 숫자를 본다)
            int returnRemaining = Mathf.CeilToInt(Mathf.Max(0f, _resultReturnCountdownEndTimeLocal - Time.time));
            _returnCountdownText.text = returnRemaining.ToString();
            // 남은 시간 RPC가 상태 변경보다 늦게 도착할 수 있어, RoundClear와 같이 매 프레임 자체 교정한다.
            ShowRoundResultStats(state);
        }
    }

    private void HandleRoundStateChanged(RoundState state)
    {
        switch (state)
        {
            case RoundState.RoundClear:
                _resultText.text = _roundClearedFormat.GetLocalizedString(RoundManager.Instance.CurrentRoundIndex + 1);
                _confirmButton.gameObject.SetActive(false); // 자동으로 다음 라운드 전환
                _returnNoticeText.SetActive(false);
                _nextRoundText.SetActive(true);
                _creditSection.SetActive(_showCreditSection);
                ShowPanel();
                break;
            case RoundState.Success:
                _resultText.text = _arrestSuccess.GetLocalizedString();
                ApplyResultReturnControls();
                _nextRoundText.SetActive(false);
                _creditSection.SetActive(false);
                ShowRoundResultStats(state);
                ShowPanel();
                break;
            case RoundState.Fail:
                _resultText.text = _arrestFailed.GetLocalizedString();
                ApplyResultReturnControls();
                _nextRoundText.SetActive(false);
                _creditSection.SetActive(false);
                ShowRoundResultStats(state);
                ShowPanel();
                break;
            default:
                _panel.SetActive(false);
                if (state != RoundState.Waiting)
                {
                    _inventoryCanvas.SetActive(true);
                }
                _nextRoundText.SetActive(false);
                GameplayUiMode.Instance?.DeactivateCursor();
                break;
        }
    }

    // 대기방 복귀는 방장만 트리거할 수 있어, 방장에게는 확인 버튼을, 나머지에게는 안내 문구를 보여준다.
    private void ApplyResultReturnControls()
    {
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        _confirmButton.gameObject.SetActive(isHost);
        _returnNoticeText.SetActive(!isHost);
    }

    // 라운드 종료 시점 남은 시간과 해당 라운드의 오검거 횟수를 표시한다.
    // RoundClear는 _roundEndTime이 다음 라운드 카운트다운으로 재사용되므로 별도 스냅샷 값을 쓰고,
    // Success/Fail은 GetRemainingTime()이 서버가 알려준 값을 그대로 반환하므로 그것을 쓴다.
    private void ShowRoundResultStats(RoundState state)
    {
        float remaining = state == RoundState.RoundClear
            ? _roundRemainingTimeAtClearLocal
            : RoundManager.Instance.GetRemainingTime();

        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);
        _remainingTimeText.text = $"{minutes:00}:{seconds:00}";

        // 결과창이 떠 있는 동안 매 프레임 호출되므로, 씬 정리 중 파괴 순서에 걸리지 않도록 확인한다.
        if (ArrestJudgementManager.Instance != null)
        {
            _wrongArrestCountText.text = ArrestJudgementManager.Instance.WrongArrestCount.ToString();
        }
    }

    private void ShowPanel()
    {
        _panel.SetActive(true);
        _inventoryCanvas.SetActive(false);
        // ESC로 내려가면 안 되는 창이라 IClosableUi로 등록하지 않고 커서만 직접 켠다.
        GameplayUiMode.Instance?.ActivateCursor();
        CaptureCriminalPortrait();
        ShowRandomClueImages();
    }

    // 촬영된 단서 이미지 중 서로 다른 것을 무작위로 골라 슬롯 수만큼 표시한다. 패널이 뜰 때마다 다시 뽑는다.
    private void ShowRandomClueImages()
    {
        if (_clueModulePreview == null || _clueImages == null || _clueImages.Length == 0) return;

        IReadOnlyList<Texture2D> capturedTextures = _clueModulePreview.CapturedTextures;

        var indices = new List<int>(capturedTextures.Count);
        for (int i = 0; i < capturedTextures.Count; i++) indices.Add(i);

        int shownCount = Mathf.Min(_clueImages.Length, indices.Count);

        // 부분 Fisher-Yates Shuffle: 필요한 개수(shownCount)만큼만 스왑해서 앞쪽에 무작위 표본을 만든다.
        for (int i = 0; i < shownCount; i++)
        {
            int randomIndex = Random.Range(i, indices.Count);
            (indices[i], indices[randomIndex]) = (indices[randomIndex], indices[i]);
        }

        for (int i = 0; i < _clueImages.Length; i++)
        {
            bool hasImage = i < shownCount;
            _clueImages[i].gameObject.SetActive(hasImage);

            if (hasImage)
            {
                _clueImages[i].texture = capturedTextures[indices[i]];
            }
        }
    }

    // 라운드 클리어/성공/실패 결과창을 띄우는 시점마다 범인 NPC를 촬영해서 보여준다.
    private void CaptureCriminalPortrait()
    {
        if (_criminalPortrait == null) return;

        NetworkObject criminalNpc = _criminalNpcManager?.CriminalNpc;
        if (criminalNpc == null)
        {
            Debug.LogError("[RoundResultPanelUI] CriminalNpcManager에서 범인 NPC를 찾지 못했습니다.");
            return;
        }

        _criminalPortrait.ShowCandidate(criminalNpc);
    }

    private void HandleConfirmButtonClicked()
    {
        // 타임아웃으로 이미 대기방 전환이 시작되면 RoundManager가 디스폰되어 RPC를 보낼 수 없다.
        // 씬 전환이 끝나기 전 몇 프레임 동안 버튼이 계속 눌리므로 여기서 막는다.
        if (RoundManager.Instance == null || !RoundManager.Instance.IsSpawned) return;

        RoundManager.Instance.ConfirmResultServerRpc();
    }
}
