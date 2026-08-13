using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

// 미션 화면 아래에 살짝 걸쳐 두는 설명서 손잡이다.
// 커서를 올리면 살짝 올라오고, 누르면 그 미션 전용 설명 패널이 펼쳐진다.
// 아이템으로 줍는 공용 가이드북(GuideBook)과는 별개다.
[RequireComponent(typeof(RectTransform))]
public sealed class MissionGuideBookTab : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("동작 모드")]
    // 켜면 미션 설명서가 아니라 공용 가이드북(GuideBook)을 여는 손잡이가 된다.
    // 이 경우 Tab으로 정보 허브를 열었을 때 살짝 올라오고, 누르면 가이드북이 열린다.
    [SerializeField] private bool _isGuideBook;

    [Header("올라오는 연출")]
    // 커서를 올렸을 때 위로 올라오는 높이다.
    [SerializeField] private float _hoverRise = 42f;
    [SerializeField] private float _riseDuration = 0.16f;

    [Header("설명 패널")]
    // 이 미션의 설명이 담긴 패널이다. 평소에는 꺼 두고 손잡이를 누를 때만 켠다.
    [SerializeField] private GameObject _guidePanel;
    // 펼쳐질 때 커지는 대상이다. 보통 패널 안의 내용 박스를 넣는다.
    [SerializeField] private RectTransform _guideContent;
    [SerializeField] private float _openDuration = 0.22f;

    private RectTransform _rect;
    // 처음 놓인 자리를 기억해 두고 그 자리를 기준으로 올렸다 내린다.
    private Vector2 _restPosition;
    private Tween _riseTween;
    private Tween _openTween;

    private void Awake()
    {
        _rect = (RectTransform)transform;
        _restPosition = _rect.anchoredPosition;
        _guidePanel?.SetActive(false);
    }

    private void OnEnable()
    {
        if (_isGuideBook)
        {
            InfoHubController.HubStateChanged += HandleHubStateChanged;
            HandleHubStateChanged(InfoHubController.IsHubOpen);
        }
    }

    private void OnDisable()
    {
        if (_isGuideBook)
        {
            InfoHubController.HubStateChanged -= HandleHubStateChanged;
        }

        // 올라간 채로 꺼지면 다시 켤 때 어긋난 자리에서 시작한다.
        _riseTween?.Kill();
        _openTween?.Kill();
        if (_rect != null)
        {
            _rect.anchoredPosition = _restPosition;
        }
    }

    private void OnDestroy()
    {
        _riseTween?.Kill();
        _openTween?.Kill();
    }

    public void OnPointerEnter(PointerEventData eventData) => MoveTo(_restPosition.y + _hoverRise);

    public void OnPointerExit(PointerEventData eventData)
    {
        bool keepRaised = _isGuideBook && InfoHubController.IsHubOpen;
        MoveTo(keepRaised ? _restPosition.y + _hoverRise : _restPosition.y);
    }

    public void OnPointerClick(PointerEventData eventData) => OpenGuide();

    // 버튼으로 열고 싶을 때 인스펙터에서 직접 연결할 수도 있다.
    public void OnGuideBookButtonClick() => OpenGuide();

    // 설명 패널의 닫기 버튼에서 직접 연결한다.
    public void OnGuideCloseButtonClick()
    {
        _openTween?.Kill();
        _guidePanel?.SetActive(false);
    }

    private void MoveTo(float targetY)
    {
        _riseTween?.Kill();
        _riseTween = _rect.DOAnchorPosY(targetY, _riseDuration).SetEase(Ease.OutQuad);
    }

    // 가이드북 모드에서는 Tab으로 허브가 열려 있는 동안만 손잡이가 올라와 있는다.
    private void HandleHubStateChanged(bool hubOpen)
    {
        MoveTo(hubOpen ? _restPosition.y + _hoverRise : _restPosition.y);

        // 허브를 닫으면 열어 둔 가이드북도 접히면서 같이 닫힌다.
        if (!hubOpen)
        {
            CloseGuideBookWithAnimation();
        }
    }

    // 가이드북은 미션 설명 패널과 같은 방식으로 작게 시작해 제자리 크기로 커진다.
    private void OpenGuideBookWithAnimation()
    {
        GuideBook guideBook = GetGuideBook();
        if (guideBook == null)
        {
            Debug.LogError("[MissionGuideBookTab] 씬에서 가이드북을 찾지 못했습니다.", this);
            return;
        }

        MoveTo(_restPosition.y + _hoverRise);
        guideBook.Show();

        RectTransform content = GetGuideBookContent(guideBook);
        if (content == null)
        {
            return;
        }

        _openTween?.Kill();
        content.localScale = Vector3.one * 0.85f;
        _openTween = content.DOScale(1f, _openDuration).SetEase(Ease.OutBack);
    }

    private void CloseGuideBookWithAnimation()
    {
        GuideBook guideBook = GetGuideBook();
        if (guideBook == null || !guideBook.gameObject.activeSelf)
        {
            return;
        }

        RectTransform content = GetGuideBookContent(guideBook);
        if (content == null)
        {
            guideBook.Close();
            return;
        }

        _openTween?.Kill();
        _openTween = content.DOScale(0.85f, _openDuration * 0.7f).SetEase(Ease.InBack)
            .OnComplete(() =>
            {
                guideBook.Close();
                content.localScale = Vector3.one;
            });
    }

    // 연출 대상은 가이드북 루트다. 인스펙터에서 따로 지정했으면 그것을 쓴다.
    private RectTransform GetGuideBookContent(GuideBook guideBook)
    {
        return _guideContent != null ? _guideContent : guideBook.transform as RectTransform;
    }

    private GuideBook GetGuideBook()
    {
        return _guidePanel != null
            ? _guidePanel.GetComponent<GuideBook>()
            : FindFirstObjectByType<GuideBook>(FindObjectsInactive.Include);
    }

    private void OpenGuide()
    {
        // 가이드북 모드는 미션 설명 패널이 아니라 씬의 공용 가이드북을 연다.
        if (_isGuideBook)
        {
            OpenGuideBookWithAnimation();
            return;
        }

        if (_guidePanel == null)
        {
            Debug.LogWarning("[MissionGuideBookTab] 설명 패널이 연결되지 않았습니다.", this);
            return;
        }

        // 커서를 올려 둔 상태로 열리므로 손잡이는 원래 자리로 되돌린다.
        MoveTo(_restPosition.y);
        _guidePanel.SetActive(true);

        if (_guideContent == null)
        {
            return;
        }

        // 작게 시작해 제자리 크기로 커지는 연출.
        _openTween?.Kill();
        _guideContent.localScale = Vector3.one * 0.85f;
        _openTween = _guideContent.DOScale(1f, _openDuration).SetEase(Ease.OutBack);
    }
}
