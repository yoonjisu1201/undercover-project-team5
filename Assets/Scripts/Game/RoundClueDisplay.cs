using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;

// 라운드 남은 시간은 필드 HUD(RoundTimerDisplay)에 이미 나오므로, HQ 화면은 같은 자리를
// 단서 진행률("{0} 라운드" / "단서 수집 현황" / "N / N")로 재활용한다. 가운데 줄은 고정 문구라 스크립트에서 관리하지 않는다.
public class RoundClueDisplay : MonoBehaviour {

	[Header("=== 단서 진행률 ===")]
	[SerializeField] private CriminalNpcManager _criminalNpcManager;
	[SerializeField] private TMP_Text _roundText;
	[SerializeField] private TMP_Text _clueCountText;
	[SerializeField] private LocalizedString _roundLabel;

	private LocalizeStringEvent _roundLocalizer;
	private PlayerClueBook _boundClueBook;

	private void Start() {
		_roundLocalizer = _roundText.GetComponent<LocalizeStringEvent>();
		_criminalNpcManager.OnCriminalAssigned += RefreshClueProgress;

		if (RoundManager.Instance != null) {
			RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
			RoundManager.Instance.OnRoundStarted += HandleRoundStarted;

			// 이벤트 구독 전에 이미 라운드가 시작됐을 수도 있으므로 현재 상태 즉시 반영
			HandleRoundStateChanged(RoundManager.Instance.CurrentState);
			if (RoundManager.Instance.CurrentState == RoundState.InRound) {
				HandleRoundStarted(RoundManager.Instance.CurrentRoundIndex);
			}
		}
	}

	private void Update() {
		RebindClueBook();
	}

	// 내 플레이어는 접속 이후에 스폰되므로 매번 최신 것을 확인해 연결한다.
	private void RebindClueBook() {
		PlayerClueBook local = PlayerClueBook.Local;
		if (_boundClueBook == local) {
			return;
		}

		if (_boundClueBook != null) {
			_boundClueBook.OnClueBookChanged -= RefreshClueProgress;
		}

		_boundClueBook = local;

		if (_boundClueBook != null) {
			_boundClueBook.OnClueBookChanged += RefreshClueProgress;
		}

		RefreshClueProgress();
	}

	private void RefreshClueProgress() {
		int captured = _boundClueBook?.ClueNumbers.Count ?? 0;
		int total = ClueCaptureRules.CalculateCapturableClueCount(_criminalNpcManager.CriminalFeature);
		_clueCountText.text = $"{captured} / {total}";
	}

	private void OnDestroy() {
		_criminalNpcManager.OnCriminalAssigned -= RefreshClueProgress;

		if (_boundClueBook != null) {
			_boundClueBook.OnClueBookChanged -= RefreshClueProgress;
		}

		if (RoundManager.Instance != null) {
			RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
			RoundManager.Instance.OnRoundStarted -= HandleRoundStarted;
		}
	}

	private void HandleRoundStateChanged(RoundState state)
	{
		if (state == RoundState.InRound) return; // 라운드 번호는 HandleRoundStarted에서 처리

		_roundLocalizer.enabled = false;
		_roundText.text = string.Empty;
		_clueCountText.text = string.Empty;
	}

	private void HandleRoundStarted(int roundIndex)
	{
		_roundLocalizer.enabled = true;
		_roundLocalizer.StringReference = _roundLabel;
		_roundLocalizer.StringReference.Arguments = new object[] { roundIndex + 1 };
		_roundLocalizer.RefreshString();
	}
}