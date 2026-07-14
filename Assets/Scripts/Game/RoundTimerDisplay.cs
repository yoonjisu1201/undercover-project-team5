using TMPro;
using UnityEngine;

public class RoundTimerDisplay : MonoBehaviour
{
	[SerializeField] private TMP_Text _timerText;
	[SerializeField] private TMP_Text _roundText;
	
	protected TMP_Text TimerText => _timerText;
	protected TMP_Text RoundText => _roundText;

	protected virtual void Start()
	{
		RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
		
		// 이벤트 구독 전에 이미 라운드가 시작됐을 수도 있으므로 현재 상태 즉시 반영
		HandleRoundStateChanged(RoundManager.Instance.CurrentState);
	}

	private void OnDestroy()
	{
		if (RoundManager.Instance != null)
		{
			RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
		}
	}

	protected virtual void Update()
	{
		if (RoundManager.Instance == null) return;

		float remaining = RoundManager.Instance.GetRemainingTime();
		int minutes = Mathf.FloorToInt(remaining / 60f);
		int seconds = Mathf.FloorToInt(remaining % 60f);
		_timerText.text = $"{minutes:00}:{seconds:00}";
	}

	protected virtual void HandleRoundStateChanged(RoundState state)
	{
		_roundText.text = state switch
		{
			RoundState.Round1 => "Round 1",
			RoundState.Round2 => "Round 2",
			_ => string.Empty
		};
	}
}
