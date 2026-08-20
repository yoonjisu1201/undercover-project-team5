using DG.Tweening;
using TMPro;
using UnityEngine;

public class RoundTimerDisplay : MonoBehaviour
{
	[SerializeField] private TMP_Text _timerText;
	[SerializeField] private TMP_Text _roundText;

	[Header("=== 오검거 시간 감소 효과 ===")]
	[SerializeField] private bool _useWrongArrestFlash;
	[SerializeField] private Color _wrongArrestFlashColor = Color.red;
	[SerializeField, Min(0.01f)] private float _wrongArrestFlashDuration = 0.15f;
	[SerializeField, Min(1)] private int _wrongArrestFlashCount = 3;

	private Tween _wrongArrestFlashTween;
	
	protected TMP_Text TimerText => _timerText;
	protected TMP_Text RoundText => _roundText;

	protected virtual void Start()
	{
		RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
		RoundManager.Instance.OnRoundStarted += HandleRoundStarted;
		ArrestJudgementManager.Instance.OnJudged += HandleArrestJudged;

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

		if (ArrestJudgementManager.Instance != null)
		{
			ArrestJudgementManager.Instance.OnJudged -= HandleArrestJudged;
		}

		_wrongArrestFlashTween?.Kill();
	}

	private void HandleArrestJudged(
		ArrestResult result,
		Unity.Netcode.NetworkObject candidate)
	{
		if (!_useWrongArrestFlash || result != ArrestResult.WrongTarget)
		{
			return;
		}

		// 이전 효과가 재생 중이면 원래 색상으로 마친 후 새 효과를 시작한다.
		_wrongArrestFlashTween?.Kill(true);

		_wrongArrestFlashTween = _timerText
			.DOColor(_wrongArrestFlashColor, _wrongArrestFlashDuration)
			.SetLoops(_wrongArrestFlashCount * 2, LoopType.Yoyo)
			.SetUpdate(true)
			.SetLink(gameObject);
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
