using System;
using UnityEngine;
using UnityEngine.Localization.Components;

public class RoundTimerDisplay_HQ : RoundTimerDisplay {
	
	private LocalizeStringEvent _roundLocalizer;

	private void Awake() {
		_roundLocalizer = RoundText.GetComponent<LocalizeStringEvent>();
	}

	// 남은 시간 비율이 줄어들수록(=종료가 가까워질수록) 흰색에서 빨간색으로 보간
	protected override void Update() {
		base.Update();

		float roundDuration = RoundManager.Instance.RoundDuration;
		float remaining = RoundManager.Instance.GetRemainingTime();

		TimerText.color = Color.Lerp(
			new Color(1f, 1f, 1f, 1f),
			new Color(1f, 0f, 0f, 1f),
			roundDuration == 0 ? 0 : 1f - remaining / roundDuration);
	}

	// HQ 화면은 라운드 텍스트를 "{0} 라운드" 로컬라이즈 문자열로 표시하므로 숫자 인자만 갱신
	protected override void HandleRoundStateChanged(RoundState state)
	{
		if (state == RoundState.InRound) return; // 라운드 번호는 HandleRoundStarted에서 처리

		_roundLocalizer.enabled = false;
		RoundText.text = string.Empty;
	}

	protected override void HandleRoundStarted(int roundIndex)
	{
		_roundLocalizer.enabled = true;
		_roundLocalizer.StringReference.Arguments = new object[] { roundIndex + 1 };
		_roundLocalizer.RefreshString();
	}
}