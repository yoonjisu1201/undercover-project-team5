using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 단서 목록의 항목 하나. 단서 번호 또는 가이드북을 나타낸다.
[RequireComponent(typeof(CanvasGroup))]
public sealed class ClueBookEntry : MonoBehaviour
{
    [SerializeField] private Button _button;
    [SerializeField] private TMP_Text _label;
    [SerializeField] private TMP_Text _subLabel;    // 오른쪽 끝에 붙는 부위명 등 보조 문구
    [SerializeField] private RawImage _thumbnail;

    [Header("등장 연출")]
    [SerializeField] private float _slideOffset = 40f;   // 왼쪽에서 이만큼 밀려 들어온다
    [SerializeField, Min(0f)] private float _duration = 0.22f;

    private CanvasGroup _group;
    private RectTransform _rect;
    private Vector2 _restPosition;
    private Tween _tween;

    private void Awake()
    {
        _group = GetComponent<CanvasGroup>();
        _rect = (RectTransform)transform;
    }

    private void OnDisable()
    {
        _tween?.Kill();
    }

    // 항목 문구와 미리보기를 채우고, 눌렀을 때 실행할 동작을 연결한다.
    public void Bind(string label, string subLabel, Texture thumbnail, System.Action onClick)
    {
        if (_label != null)
        {
            _label.text = label;
        }

        if (_subLabel != null)
        {
            _subLabel.text = subLabel;
        }

        if (_thumbnail != null)
        {
            _thumbnail.texture = thumbnail;
            _thumbnail.gameObject.SetActive(thumbnail != null);
        }

        _button.onClick.RemoveAllListeners();
        _button.interactable = onClick != null;

        if (onClick != null)
        {
            _button.onClick.AddListener(() => onClick.Invoke());
        }
    }

    // 목록이 펼쳐질 때 왼쪽에서 하나씩 밀려 들어온다.
    public void PlayIn(float delay)
    {
        // 레이아웃이 자리를 잡은 뒤의 위치를 기준으로 삼는다.
        _restPosition = _rect.anchoredPosition;

        _tween?.Kill();
        _group.alpha = 0f;
        _rect.anchoredPosition = _restPosition + Vector2.left * _slideOffset;

        _tween = DOTween.Sequence()
            .AppendInterval(delay)
            .Append(_rect.DOAnchorPos(_restPosition, _duration).SetEase(Ease.OutCubic))
            .Join(_group.DOFade(1f, _duration));
    }

    // 목록이 접힐 때 왼쪽으로 빠지며 사라진다.
    public void PlayOut()
    {
        _tween?.Kill();
        _tween = DOTween.Sequence()
            .Append(_rect.DOAnchorPos(_restPosition + Vector2.left * _slideOffset, _duration).SetEase(Ease.InCubic))
            .Join(_group.DOFade(0f, _duration));
    }
}
