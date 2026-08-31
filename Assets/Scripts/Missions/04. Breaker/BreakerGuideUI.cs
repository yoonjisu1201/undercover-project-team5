using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;

// 배전반 진행 가이드를 우측 하단 전용 카드에 표시한다.
// 짧은 시스템 알림용 NoticeUI와 분리해 두 문구가 서로 덮어쓰지 않게 한다.
public sealed class BreakerGuideUI : MonoBehaviour
{
    public static BreakerGuideUI Instance { get; private set; }

    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private TMP_Text _titleLabel;
    [SerializeField] private TMP_Text _bodyLabel;
    [SerializeField, Min(0f)] private float _fadeDuration = 0.2f;
    [SerializeField, Min(0f)] private float _holdSeconds = 4.5f;

    private Sequence _sequence;

    // 씬의 전용 UI 인스턴스를 등록하고 최초에는 보이지 않게 초기화한다.
    private void Awake()
    {
        Instance = this;
        _canvasGroup.alpha = 0f;
        _canvasGroup.gameObject.SetActive(false);
    }

    // 씬 전환으로 현재 UI가 사라질 때 정적 참조와 실행 중인 연출을 정리한다.
    private void OnDestroy()
    {
        _sequence?.Kill();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    // 현재 언어의 제목과 본문을 읽어 전용 카드에 일정 시간 표시한다.
    public void ShowGuide(LocalizedString localizedTitle, LocalizedString localizedBody)
    {
        if (localizedTitle == null || localizedBody == null)
        {
            return;
        }

        ShowGuide(localizedTitle.GetLocalizedString(), localizedBody.GetLocalizedString());
    }

    // 진행 중인 배전반 가이드만 교체하고 NoticeUI의 시스템 알림에는 영향을 주지 않는다.
    private void ShowGuide(string title, string body)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        _sequence?.Kill();
        _titleLabel.text = title;
        _bodyLabel.text = body;
        _canvasGroup.gameObject.SetActive(true);
        _canvasGroup.alpha = 0f;

        _sequence = DOTween.Sequence()
            .Append(_canvasGroup.DOFade(1f, _fadeDuration))
            .AppendInterval(_holdSeconds)
            .Append(_canvasGroup.DOFade(0f, _fadeDuration))
            .AppendCallback(() => _canvasGroup.gameObject.SetActive(false));
    }
}
