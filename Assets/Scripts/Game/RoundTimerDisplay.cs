using TMPro;
using UnityEngine;

public class RoundTimerDisplay : MonoBehaviour
{
	[SerializeField] private TMP_Text _timerText;
	[SerializeField] private TMP_Text _roundText;

	private void Start()
	{
		RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
	}

	private void OnDestroy()
	{
		if (RoundManager.Instance != null)
		{
			RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
		}
	}

	private void Update()
	{
		if (RoundManager.Instance == null) return;

		float remaining = RoundManager.Instance.GetRemainingTime();
		int minutes = Mathf.FloorToInt(remaining / 60f);
		int seconds = Mathf.FloorToInt(remaining % 60f);
		_timerText.text = $"{minutes:00}:{seconds:00}";
	}

	private void HandleRoundStateChanged(RoundState state)
	{
		_roundText.text = state switch
		{
			RoundState.Round1 => "Round 1",
			RoundState.Round2 => "Round 2",
			_ => string.Empty
		};
	}
}
