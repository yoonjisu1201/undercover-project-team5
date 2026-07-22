using UnityEngine;

// 게임씬 로드 완료 직후부터, NPC/단서 스폰이 끝나 RoundManager가 Round1을 시작할 때까지 로딩 패널을 보여준다.
public class GameSceneLoadingUI : MonoBehaviour
{
    [SerializeField] private GameObject _loadingPanel;
    [SerializeField] private GameObject _inventoryCanvas;

    private void Start()
    {
        _loadingPanel.SetActive(true);
        _inventoryCanvas.SetActive(false);

        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged += HandleRoundReadyOnce;
        }
    }

    private void OnDestroy()
    {
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundReadyOnce;
        }
    }

    // 스폰이 끝나 라운드가 Waiting을 벗어나는 첫 순간에만 반응하고 구독을 해제한다.
    private void HandleRoundReadyOnce(RoundState state)
    {
        if (state == RoundState.Waiting) return;

        RoundManager.Instance.OnRoundStateChanged -= HandleRoundReadyOnce;
        _loadingPanel.SetActive(false);
        _inventoryCanvas.SetActive(true);
    }
}
