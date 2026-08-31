using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;

// 라운드 목표와 진행 방향을 우측 하단 공용 가이드 카드에 표시한다.
public sealed class GuideUI : MonoBehaviour
{
    public static GuideUI Instance { get; private set; }

    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private TMP_Text _titleLabel;
    [SerializeField] private TMP_Text _bodyLabel;
    [SerializeField, Min(0f)] private float _fadeDuration = 0.2f;
    [SerializeField, Min(0f)] private float _holdSeconds = 4.5f;

    private Sequence _sequence;

    // 씬의 공용 가이드 UI 인스턴스를 등록하고 최초에는 보이지 않게 초기화한다.
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

    // 현재 언어의 제목과 본문을 읽어 공용 카드에 일정 시간 표시한다.
    public void ShowGuide(LocalizedString localizedTitle, LocalizedString localizedBody)
    {
        if (localizedTitle == null || localizedBody == null)
        {
            return;
        }

        string title = localizedTitle.GetLocalizedString();
        string body = localizedBody.GetLocalizedString();

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
