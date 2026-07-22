using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
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
    [SerializeField] private TMP_Text _remainingTimeText; // 라운드 종료 시점 남은 시간 표시용
    [SerializeField] private TMP_Text _wrongArrestCountText; // 해당 라운드의 오검거 횟수 표시용
    [SerializeField] private ArrestCandidatePortrait _criminalPortrait; // 검거 투표 때 쓰는 것을 그대로 재사용
    [SerializeField] private CriminalNpcManager _criminalNpcManager; // static Instance가 없어 인스펙터에서 직접 연결
    [SerializeField] private ClueModulePreview _clueModulePreview; // 촬영된 단서 이미지 참조용, static Instance가 없어 인스펙터에서 직접 연결
    [SerializeField] private RawImage[] _clueImages; // 무작위로 뽑은 단서 이미지를 표시할 슬롯

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
                ShowRoundResultStats(state);
                ShowPanel();
                break;
            case RoundState.Success:
                _resultText.text = "검거 성공";
                _confirmButton.gameObject.SetActive(true);
                _nextRoundText.SetActive(false);
                ShowRoundResultStats(state);
                ShowPanel();
                break;
            case RoundState.Fail:
                _resultText.text = "검거 실패";
                _confirmButton.gameObject.SetActive(true);
                _nextRoundText.SetActive(false);
                ShowRoundResultStats(state);
                ShowPanel();
                break;
            default:
                _panel.SetActive(false);
                _inventoryCanvas.SetActive(true);
                _nextRoundText.SetActive(false);
                break;
        }
    }

    // 라운드 종료 시점 남은 시간과 해당 라운드의 오검거 횟수를 표시한다.
    // Round1Clear는 _roundEndTime이 다음 라운드 카운트다운으로 재사용되므로 별도 스냅샷 값을 쓰고,
    // Success/Fail은 전환 시점에 멈춰있는 CachedRemainingTime을 그대로 쓴다.
    private void ShowRoundResultStats(RoundState state)
    {
        float remaining = state == RoundState.Round1Clear
            ? RoundManager.Instance.Round1RemainingTimeAtClear
            : RoundManager.Instance.CachedRemainingTime;

        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);
        _remainingTimeText.text = $"{minutes:00}:{seconds:00}";

        _wrongArrestCountText.text = ArrestJudgementManager.Instance.WrongArrestCount.ToString();
    }

    private void ShowPanel()
    {
        _panel.SetActive(true);
        _inventoryCanvas.SetActive(false);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
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

    // 1라운드 클리어/성공/실패 결과창을 띄우는 시점마다 범인 NPC를 촬영해서 보여준다.
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
        RoundManager.Instance.ConfirmResultServerRpc();
    }
}