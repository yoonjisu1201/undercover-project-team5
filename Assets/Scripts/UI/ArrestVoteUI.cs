using TMPro;
using UnityEngine;

public class ArrestVoteUI : MonoBehaviour
{
    [SerializeField] private TMP_Text _remainingVoteText;  //남은 검거 투표 횟수

    private void Start()
    {
        //검거 횟수가 변경될때마다 UI 변경하기 위한 이벤트 구독
        ArrestVoteManager.Instance.OnRemainingVoteAttemptsChanged += HandleRemainingVoteAttemptsChanged;

        // 구독 전에 이미 값이 세팅돼 있을 수 있으므로 현재 값 즉시 반영
        HandleRemainingVoteAttemptsChanged(ArrestVoteManager.Instance.RemainingVoteAttempts);
    }

    private void OnDestroy()
    {
        if (ArrestVoteManager.Instance != null)
        {
            ArrestVoteManager.Instance.OnRemainingVoteAttemptsChanged -= HandleRemainingVoteAttemptsChanged;
        }
    }

    private void HandleRemainingVoteAttemptsChanged(int remaining)
    {
        _remainingVoteText.text = remaining.ToString();
    }
}
