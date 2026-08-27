using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

// 게임 시작 시 SVC에 기록된 셰이더 variant를 미리 컴파일해, 이후 씬 전환 중 컴파일 hitch를 방지한다.
// WarmUpProgressively로 매 프레임 일부씩만 컴파일해 로딩 패널이 멈추지 않고 진행률을 보여주게 한다.
public class ShaderPrecompileWarmup : MonoBehaviour
{
	private const int VariantsPerFrame = 4;

	[SerializeField] private ShaderVariantCollection _collection;
	[SerializeField] private GameObject _loadingPanel;
	[SerializeField] private TMP_Text _progressText;

	private static ShaderPrecompileWarmup s_instance;

	private void Awake()
	{
		if (s_instance != null && s_instance != this)
		{
			Destroy(gameObject);
			return;
		}
		s_instance = this;

		DontDestroyOnLoad(gameObject);
		WarmUpAsync().Forget();
	}

	private async UniTaskVoid WarmUpAsync()
	{
		if (_collection == null) return;

		if (_loadingPanel != null) _loadingPanel.SetActive(true);

		// 패널을 켠 프레임에 바로 워밍업하면 첫 화면이 그려지기 전 작업 부하가 몰릴 수 있다.
		// 한 프레임을 넘겨 로딩 UI가 먼저 렌더되도록 보장한다.
		await UniTask.NextFrame();

		try
		{
			bool done = false;
			while (!done)
			{
				done = _collection.WarmUpProgressively(VariantsPerFrame);

				if (_progressText != null)
				{
					_progressText.text = $"{_collection.warmedUpVariantCount} / {_collection.variantCount}";
				}

				await UniTask.Yield(PlayerLoopTiming.Update);
			}
		}
		finally
		{
			// 워밍업 중 예외가 나도 입력을 막는 패널이 화면에 남지 않도록 항상 정리한다.
			if (_loadingPanel != null) _loadingPanel.SetActive(false);
		}
	}
}
