using System.Collections.Generic;
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
    // 정보 허브(Tab)와는 무관하게 동작한다. 손잡이를 누르거나 H 를 누르면 열고 닫는다.
    [SerializeField] private bool _isGuideBook;

    // 공용 가이드북 손잡이인지. 미션 화면과 겹칠 때 숨길 대상을 밖에서 가려내는 데 쓴다.
    public bool IsGuideBookHandle => _isGuideBook;

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

    // 작게 시작해 제자리 크기로 커진다. 여는 연출과 닫는 연출이 같은 값을 써야 되감기로 보인다.
    private const float PopStartScale = 0.85f;

    private RectTransform _rect;
    // 처음 놓인 자리를 기억해 두고 그 자리를 기준으로 올렸다 내린다.
    private Vector2 _restPosition;
    private Tween _riseTween;
    private Tween _openTween;

    // 가이드북 모드에서만 쓴다. H 로 여닫고, 어떤 경로로 닫히든 손잡이를 내리기 위해 붙잡아 둔다.
    private CustomInputActions _actions;
    private GuideBook _boundGuideBook;

    // 확대·축소할 대상. 딤을 빼고 나면 하나가 아닐 수 있어 목록으로 들고 있는다.
    private readonly List<RectTransform> _popTargets = new();

    private void Awake()
    {
        _rect = (RectTransform)transform;
        _restPosition = _rect.anchoredPosition;
        _guidePanel?.SetActive(false);
    }

    private void OnEnable()
    {
        if (!_isGuideBook)
        {
            return;
        }

        _actions ??= new CustomInputActions();
        _actions.UI.Enable();
        BindGuideBook();
    }

    private void OnDisable()
    {
        if (_isGuideBook)
        {
            _actions?.UI.Disable();
            UnbindGuideBook();
        }

        // 올라간 채로 꺼지면 다시 켤 때 어긋난 자리에서 시작한다.
        _riseTween?.Kill();
        _openTween?.Kill();
        if (_rect != null)
        {
            _rect.anchoredPosition = _restPosition;
        }
    }

    // 손잡이를 누른 것과 똑같이 H 로도 여닫는다. 연출도 그대로 쓴다.
    private void Update()
    {
        if (!_isGuideBook || _boundGuideBook == null)
        {
            return;
        }

        // 글자를 치는 중이면 H 는 입력창의 것이다. 닉네임에 h 를 넣을 때 가이드북이 열렸다.
        if (GameplayUiMode.IsTypingText || !_actions.UI.OpenGuideBook.WasPressedThisFrame())
        {
            return;
        }

        if (_boundGuideBook.gameObject.activeSelf)
        {
            CloseGuideBookWithAnimation();
            return;
        }

        OpenGuideBookWithAnimation();
    }

    private void BindGuideBook()
    {
        _boundGuideBook = GetGuideBook();
        if (_boundGuideBook == null)
        {
            Debug.LogError("[MissionGuideBookTab] 씬에서 가이드북을 찾지 못했습니다.", this);
            return;
        }

        // 단축키는 이쪽이 맡는다. 가이드북이 같은 키를 함께 읽으면 닫는 연출이 시작되기도 전에 꺼진다.
        _boundGuideBook.HotkeyHandledExternally = true;
        _boundGuideBook.OnClose += HandleGuideBookClosed;
    }

    private void UnbindGuideBook()
    {
        if (_boundGuideBook == null)
        {
            return;
        }

        _boundGuideBook.HotkeyHandledExternally = false;
        _boundGuideBook.OnClose -= HandleGuideBookClosed;
        _boundGuideBook = null;
    }

    // X 버튼이나 ESC 로 닫힌 경우에도 손잡이는 제자리로 내려와야 한다.
    private void HandleGuideBookClosed() => MoveTo(_restPosition.y);

    private void OnDestroy()
    {
        _riseTween?.Kill();
        _openTween?.Kill();
    }

    public void OnPointerEnter(PointerEventData eventData) => MoveTo(_restPosition.y + _hoverRise);

    public void OnPointerExit(PointerEventData eventData) => MoveTo(_restPosition.y);

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

    // 가이드북은 미션 설명 패널과 같은 방식으로 작게 시작해 제자리 크기로 커진다.
    // 손잡이도 함께 살짝 올라온다.
    private void OpenGuideBookWithAnimation()
    {
        GuideBook guideBook = _boundGuideBook != null ? _boundGuideBook : GetGuideBook();
        if (guideBook == null)
        {
            Debug.LogError("[MissionGuideBookTab] 씬에서 가이드북을 찾지 못했습니다.", this);
            return;
        }

        MoveTo(_restPosition.y + _hoverRise);
        guideBook.Show();

        CollectPopTargets(guideBook);
        if (_popTargets.Count == 0)
        {
            return;
        }

        _openTween?.Kill();
        Sequence pop = DOTween.Sequence();
        foreach (RectTransform target in _popTargets)
        {
            target.localScale = Vector3.one * PopStartScale;
            pop.Join(target.DOScale(1f, _openDuration).SetEase(Ease.OutBack));
        }

        _openTween = pop;
    }

    // 여는 연출을 그대로 되감는다. 손잡이는 가이드북이 실제로 꺼질 때 OnClose 를 받아 내려간다.
    private void CloseGuideBookWithAnimation()
    {
        GuideBook guideBook = _boundGuideBook != null ? _boundGuideBook : GetGuideBook();
        if (guideBook == null || !guideBook.gameObject.activeSelf)
        {
            return;
        }

        CollectPopTargets(guideBook);
        if (_popTargets.Count == 0)
        {
            guideBook.Close();
            return;
        }

        _openTween?.Kill();
        Sequence pop = DOTween.Sequence();
        foreach (RectTransform target in _popTargets)
        {
            pop.Join(target.DOScale(PopStartScale, _openDuration * 0.7f).SetEase(Ease.InBack));
        }

        _openTween = pop.OnComplete(() =>
        {
            guideBook.Close();
            foreach (RectTransform target in _popTargets)
            {
                target.localScale = Vector3.one;
            }
        });
    }

    // 확대·축소 대상을 모은다.
    //
    // 화면 전체를 덮는 딤이 연출 대상 안에 들어 있으면 책과 함께 커져서 화면 밖으로 밀려나고,
    // 그동안 화면 가장자리가 덮이지 않아 밝아진다. 딤만 빼고 나머지 형제(그림자·클립보드)를 키운다.
    private void CollectPopTargets(GuideBook guideBook)
    {
        _popTargets.Clear();

        RectTransform content = GetGuideBookContent(guideBook);
        if (content == null)
        {
            return;
        }

        RectTransform dimmer = guideBook.FullScreenDimmer;
        if (dimmer == null || dimmer.parent != content)
        {
            _popTargets.Add(content);
            return;
        }

        for (int i = 0; i < content.childCount; i++)
        {
            if (content.GetChild(i) is RectTransform child && child != dimmer)
            {
                _popTargets.Add(child);
            }
        }
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
