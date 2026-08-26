using System.Collections.Generic;
using UnityEngine;

// 게임씬 로드 완료 직후부터, NPC/단서 스폰이 끝나 RoundManager가 Round1을 시작할 때까지 로딩 패널을 보여준다.
public class GameSceneLoadingUI : MonoBehaviour
{
    [SerializeField] private GameObject _inventoryCanvas;

	private readonly List<GameObject> _hiddenHudRoots = new();

    private void Start()
    {
        _inventoryCanvas.SetActive(false);
		HideGameplayHud<InfoHubController>();
		HideGameplayHud<MontageShareUI>();

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
        _inventoryCanvas.SetActive(true);

		foreach (GameObject hudRoot in _hiddenHudRoots)
		{
			if (hudRoot != null)
			{
				hudRoot.SetActive(true);
			}
		}

		_hiddenHudRoots.Clear();
    }

	private void HideGameplayHud<T>() where T : MonoBehaviour
	{
		T hud = FindFirstObjectByType<T>(FindObjectsInactive.Include);
		if (hud == null || !hud.gameObject.activeSelf || _hiddenHudRoots.Contains(hud.gameObject))
		{
			return;
		}

		_hiddenHudRoots.Add(hud.gameObject);
		hud.gameObject.SetActive(false);
	}
}
