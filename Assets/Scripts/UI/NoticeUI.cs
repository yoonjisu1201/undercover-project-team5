using DG.Tweening;
using TMPro;
using UnityEngine;

// 아이템/상호작용과 무관한 게임 전역 일회성 안내 메시지(예: "버릴 수 없습니다")를
// 화면 중앙 상단에 눈에 띄게 띄운다. 상호작용 힌트(InteractionPromptUI)와는 별개의 자리다.
public class NoticeUI : MonoBehaviour
{
    public static NoticeUI Instance { get; private set; }

    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private TMP_Text _label;
    [SerializeField, Min(0f)] private float _fadeDuration = 0.2f;
    [SerializeField, Min(0f)] private float _holdSeconds = 1.5f;

    private Sequence _sequence;

    private void Awake()
    {
        Instance = this;
        _canvasGroup.alpha = 0f;
        _canvasGroup.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ShowNotice(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _sequence?.Kill();
        _label.text = message;
        _canvasGroup.gameObject.SetActive(true);
        _canvasGroup.alpha = 0f;

        _sequence = DOTween.Sequence()
            .Append(_canvasGroup.DOFade(1f, _fadeDuration))
            .AppendInterval(_holdSeconds)
            .Append(_canvasGroup.DOFade(0f, _fadeDuration))
            .AppendCallback(() => _canvasGroup.gameObject.SetActive(false));
    }
}
