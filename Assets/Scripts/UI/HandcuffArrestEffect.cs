using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 검거 판정 전에 재생할 수갑 체결 연출만 담당합니다.
// 판정 로직과는 의도적으로 분리되어 있습니다.
// UI와 렌더링 오브젝트는 HandcuffArrestEffect 프리팹에 미리 구성해 두고,
// 이 스크립트는 직렬화된 참조를 사용해 연출의 재생 순서만 제어합니다.
public sealed class HandcuffArrestEffect : MonoBehaviour
{
    [Header("연출 오브젝트")]
    [SerializeField] private CanvasGroup _canvasGroup;
    // HandcuffRenderCamera가 수갑 3D 모델을 RenderTexture에 촬영하고,
    // 이 RawImage가 해당 RenderTexture를 받아 화면 중앙에 표시합니다.
    [SerializeField] private RawImage _handcuffImage;
    [SerializeField] private Image _flashImage;
    [SerializeField] private Transform _visualPivot;

    [Header("연출 시간")]
    [SerializeField, Min(0f)] private float _fadeInDuration = 0.15f;
    [SerializeField, Min(0f)] private float _approachDuration = 0.35f;
    [SerializeField, Min(0f)] private float _snapDuration = 0.5f;
    [SerializeField, Min(0f)] private float _holdDuration = 0.45f;
    [SerializeField, Min(0f)] private float _fadeOutDuration = 0.2f;

    private Tween _effectTween;
    private bool _isPlaying;

    // 외부 검거 흐름에서 연출 중복 요청을 피하거나 입력 상태를 판단할 수 있도록 공개합니다.
    public bool IsPlaying => _isPlaying;

    private void Awake()
    {
        _canvasGroup.gameObject.SetActive(false);
    }

    public async UniTask PlayAsync()
    {
        // 한 번 시작한 연출이 끝나기 전에 다시 호출되면 같은 UI와 Tween을 중복 조작하게 되므로 무시합니다.
        if (_isPlaying || _canvasGroup == null || _handcuffImage == null || _flashImage == null || _visualPivot == null)
        {
            return;
        }

        _isPlaying = true;
        _canvasGroup.gameObject.SetActive(true);
        _canvasGroup.alpha = 0f;
        // 판정 연출 중 뒤쪽 게임 UI가 클릭되지 않도록 전체 화면 Canvas가 입력을 가로챕니다.
        _canvasGroup.blocksRaycasts = true;
        _canvasGroup.interactable = true;

        _visualPivot.localScale = Vector3.one * 0.72f;
        _visualPivot.localRotation = Quaternion.Euler(0f, 0f, -12f);
        _handcuffImage.rectTransform.localScale = Vector3.one;
        SetImageAlpha(_flashImage, 0f);

        // 라운드 시간이나 Time.timeScale이 멈춘 상황에서도 판정 연출은 끝나야 하므로 unscaled time으로 재생합니다.
        Sequence sequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);
        sequence.Append(_canvasGroup.DOFade(1f, _fadeInDuration));
        sequence.Join(_visualPivot.DOScale(1f, _approachDuration).SetEase(Ease.OutBack));
        sequence.Join(_visualPivot.DOLocalRotate(Vector3.zero, _approachDuration).SetEase(Ease.OutCubic));
        sequence.Append(_visualPivot.DOScale(0.78f, _snapDuration).SetEase(Ease.InBack));
        sequence.Join(_visualPivot.DOLocalRotate(new Vector3(0f, 0f, 5f), _snapDuration).SetEase(Ease.InCubic));
        sequence.AppendCallback(PlaySnapFlash);
        sequence.Append(_visualPivot.DOScale(1.08f, 0.08f).SetEase(Ease.OutQuad));
        sequence.Join(_visualPivot.DOLocalRotate(Vector3.zero, 0.08f).SetEase(Ease.OutQuad));
        sequence.Append(_visualPivot.DOScale(1f, 0.1f).SetEase(Ease.OutQuad));
        sequence.AppendInterval(_holdDuration);
        sequence.Append(_canvasGroup.DOFade(0f, _fadeOutDuration));

        _effectTween = sequence;
        await sequence.AsyncWaitForCompletion();

        _effectTween = null;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;
        _canvasGroup.gameObject.SetActive(false);
        _isPlaying = false;
    }

    private void PlaySnapFlash()
    {
        // 수갑이 잠기는 순간 짧은 크기 반동과 화면 플래시를 함께 보여줘 찰칵하는 타격감을 만듭니다.
        _handcuffImage.rectTransform.DOPunchScale(Vector3.one * 0.08f, 0.18f, 5, 0.4f)
            .SetUpdate(true)
            .SetTarget(this);

        SetImageAlpha(_flashImage, 0.65f);
        _flashImage.DOFade(0f, 0.18f).SetUpdate(true).SetTarget(this);
    }

    private static void SetImageAlpha(Image image, float alpha)
    {
        Color color = image.color;
        color.a = alpha;
        image.color = color;
    }

    private void OnDestroy()
    {
        // 씬 전환 중 연출 오브젝트가 먼저 파괴돼도 Tween이 사라진 참조를 계속 갱신하지 않도록 정리합니다.
        _effectTween?.Kill();
        DOTween.Kill(this);
    }
}
