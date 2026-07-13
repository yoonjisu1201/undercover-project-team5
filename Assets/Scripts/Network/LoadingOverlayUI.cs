using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

// 세션 생성/조인 대기 중 로딩 오버레이를 표시한다.
// 성공 시에는 씬 전환과 함께 이 오브젝트 자체가 사라지므로 따로 숨길 필요가 없고,
// 실패(OnSessionError)한 경우에만 명시적으로 다시 숨긴다.
public class LoadingOverlayUI : MonoBehaviour
{
	[SerializeField] private GameObject _overlayPanel;
	[SerializeField] private Slider _progressSlider;

	[Header("진행률 연출")]
	[SerializeField] private float _fillDuration = 3f;  // 0 → _maxProgress까지 걸리는 시간
	[SerializeField] private float _maxProgress = 0.9f; // 실제 완료 전까지는 이 값을 넘지 않음

	private CancellationTokenSource _progressCts;

	private void Start()
	{
		GameSessionManager.Instance.OnSessionError += HandleSessionError;

		Hide();
	}

	private void OnDestroy()
	{
		if (GameSessionManager.Instance != null)
		{
			GameSessionManager.Instance.OnSessionError -= HandleSessionError;
		}

		_progressCts?.Cancel();
		_progressCts?.Dispose();
	}

	public void Show()
	{
		_overlayPanel.SetActive(true);
		_progressSlider.value = 0f;

		_progressCts?.Cancel();
		_progressCts?.Dispose();
		_progressCts = new CancellationTokenSource();
		FillProgressAsync(_progressCts.Token).Forget();
	}

	private void Hide()
	{
		_overlayPanel.SetActive(false);

		_progressCts?.Cancel();
		_progressCts?.Dispose();
		_progressCts = null;
	}

	private async UniTaskVoid FillProgressAsync(CancellationToken token)
	{
		float elapsed = 0f;

		while (elapsed < _fillDuration)
		{
			elapsed += Time.deltaTime;
			_progressSlider.value = Mathf.Lerp(0f, _maxProgress, elapsed / _fillDuration);
			await UniTask.Yield(PlayerLoopTiming.Update, token);
		}

		_progressSlider.value = _maxProgress;
	}

	private void HandleSessionError(string message)
	{
		Hide();
	}
}
