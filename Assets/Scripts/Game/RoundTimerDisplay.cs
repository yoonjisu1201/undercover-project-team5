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
		RoundManager.Instance.OnRoundStarted += HandleRoundStarted;

		// 이벤트 구독 전에 이미 라운드가 시작됐을 수도 있으므로 현재 상태 즉시 반영
		HandleRoundStateChanged(RoundManager.Instance.CurrentState);
		if (RoundManager.Instance.CurrentState == RoundState.InRound)
		{
			HandleRoundStarted(RoundManager.Instance.CurrentRoundIndex);
		}
	}

	private void OnDestroy()
	{
		if (RoundManager.Instance != null)
		{
			RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
			RoundManager.Instance.OnRoundStarted -= HandleRoundStarted;
		}
	}

	protected virtual void Update()
	{
		if (RoundManager.Instance == null) return;
		if (RoundManager.Instance.CurrentState != RoundState.InRound) return;

		float remaining = RoundManager.Instance.GetRemainingTime();
		int minutes = Mathf.FloorToInt(remaining / 60f);
		int seconds = Mathf.FloorToInt(remaining % 60f);
		_timerText.text = $"{minutes:00}:{seconds:00}";
	}

	protected virtual void HandleRoundStateChanged(RoundState state)
	{
		if (state != RoundState.InRound)
		{
			_roundText.text = string.Empty;
		}
	}

	protected virtual void HandleRoundStarted(int roundIndex)
	{
		_roundText.text = $"Round {roundIndex + 1}";
	}
}
