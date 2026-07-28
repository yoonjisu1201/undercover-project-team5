using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 카드 드래그 담당 클래스
public sealed class StationCardDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    private SubwayRouteMiniGame _owner;
    private RectTransform _rect;
    private CanvasGroup _canvasGroup;   // 드래그 중에 다른 UI 요소가 포인터 입력을 받도록 설정
    private Image _background;
    private Color _defaultColor;

    public string StationName { get; private set; }
    public int CurrentSlotIndex { get; set; } = -1; // 카드가 현재 배치된 슬롯 인덱스 (0부터 시작, -1이면 미배치 상태)

    // 카드에 게임 데이터와 기본 UI 상태를 연결한다.
    public void Initialize(SubwayRouteMiniGame owner, string stationName, Color routeColor)
    {
        _owner = owner;
        StationName = stationName;
        _rect = (RectTransform)transform;
        _canvasGroup = GetComponent<CanvasGroup>();
        _background = GetComponent<Image>();
        _defaultColor = Color.Lerp(_background.color, routeColor, 0.18f);
        _background.color = _defaultColor;
    }

    // 정답 판정 결과 색상을 카드에 적용한다.
    public void SetBackgroundColor(Color color) => _background.color = color;

    // 카드를 기본 노선 색상으로 되돌린다.
    public void SetBackgroundToDefault() => _background.color = _defaultColor;

    // 드래그 중 다른 드롭 영역이 포인터 입력을 받도록 준비한다.
    public void OnBeginDrag(PointerEventData eventData)
    {
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.alpha = 0.82f;
        transform.SetAsLastSibling();
    }

    // 포인터 위치를 카드의 로컬 좌표로 변환해 따라가게 한다.
    public void OnDrag(PointerEventData eventData)
    {
        RectTransform parent = (RectTransform)_rect.parent;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, eventData.position, eventData.pressEventCamera, out Vector2 point))
        {
            _rect.localPosition = point;
        }
    }

    // 드래그가 끝난 카드를 유효한 슬롯 또는 보기 위치로 정렬한다.
    public void OnEndDrag(PointerEventData eventData)
    {
        _canvasGroup.blocksRaycasts = true;
        _canvasGroup.alpha = 1f;
        _owner.RefreshCardPositions();
    }

    // 다른 카드가 놓이면 슬롯 교환 또는 보기 복귀를 처리한다.
    public void OnDrop(PointerEventData eventData)
    {
        StationCardDragHandler card = eventData.pointerDrag?.GetComponent<StationCardDragHandler>();
        if (card == null || card == this)   // 자기 자신이 드롭된 경우는 무시
        {
            return;
        }

        if (CurrentSlotIndex >= 0)  // 카드가 유효한 슬롯에 배치되어 있다면, 드롭된 카드와 슬롯을 교환한다.
        {
            _owner.PlaceCard(card, CurrentSlotIndex);
        }
        else
        {
            _owner.ReturnCardToPool(card);
        }
    }
}

// 카드 드롭 담당 클래스
public sealed class StationDropSlot : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    private SubwayRouteMiniGame _owner;
    private int _slotIndex;
    private Image _background;
    private Text _placeholder;
    private Color _normalColor;
    private bool _isOccupied;

    // 드롭 슬롯에 순서와 표시 요소를 연결한다.
    public void Initialize(
        SubwayRouteMiniGame owner,
        int slotIndex,
        Image background,
        Text placeholder)
    {
        _owner = owner;
        _slotIndex = slotIndex;
        _background = background;
        _placeholder = placeholder;
        _normalColor = background.color;
    }

    // 드롭된 역 카드를 이 슬롯에 배치한다.
    public void OnDrop(PointerEventData eventData)
    {
        StationCardDragHandler card = eventData.pointerDrag?.GetComponent<StationCardDragHandler>();
        if (card != null)
        {
            _owner.PlaceCard(card, _slotIndex);
        }
    }

    // 카드가 슬롯 위에 들어오면 배치 가능 색상을 표시한다.
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (eventData.pointerDrag?.GetComponent<StationCardDragHandler>() != null)
        {
            _background.color = new Color(0.2f, 0.75f, 0.58f, 0.65f);
        }
    }

    // 포인터가 슬롯을 벗어나면 기본 색상으로 복원한다.
    public void OnPointerExit(PointerEventData eventData) => RestoreColor();

    // 슬롯 점유 상태에 맞춰 안내 문구를 표시한다.
    public void SetOccupied(bool occupied)
    {
        _isOccupied = occupied;
        _placeholder.gameObject.SetActive(!occupied);
        RestoreColor();
    }

    // 비어 있는 필수 슬롯을 오류 색상으로 표시한다.
    public void ShowError()
    {
        if (!_isOccupied)
        {
            _background.color = new Color(0.72f, 0.12f, 0.15f, 0.7f);
        }
    }

    // 슬롯을 현재 점유 상태에 맞는 기본 색상으로 되돌린다.
    private void RestoreColor()
    {
        _background.color = _isOccupied
            ? new Color(_normalColor.r, _normalColor.g, _normalColor.b, 0.08f)
            : _normalColor;
    }
}
