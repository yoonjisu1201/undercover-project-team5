using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Scripting;
using UnityEngine.UI;

public interface IUIDragDropContext
{
    void DropOnSlot(UIDraggableItem item, int slotIndex);
    void DropOnItem(UIDraggableItem target, UIDraggableItem dragged);
    void ReturnToPool(UIDraggableItem item);
    void RefreshItemPositions();
}

[Preserve]
public class UIDraggableItem : MonoBehaviour,
    IInitializePotentialDragHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler,
    IDropHandler
{
    private IUIDragDropContext _context;
    private RectTransform _rect;
    private RectTransform _dragRoot;
    private Canvas _rootCanvas;
    private CanvasGroup _canvasGroup;
    private Vector2 _dragOffset;
    private Camera _dragEventCamera;

    public int CurrentSlotIndex { get; set; } = -1;
    public object Payload { get; private set; }

    // 해상도 및 DPI에 따른 EventSystem 드래그 임계값 차이 없이 즉시 드래그를 시작합니다.
    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        eventData.useDragThreshold = false;
    }

    protected void InitializeDrag(IUIDragDropContext context, object payload)
    {
        _context = context;
        Payload = payload;
        _rect = (RectTransform)transform;
        _rootCanvas = GetComponentInParent<Canvas>().rootCanvas;
        _dragRoot = (RectTransform)_rootCanvas.transform;
        _canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.alpha = 0.82f;

        // 늘어나는 앵커(anchorMin != anchorMax)를 쓰는 항목은 부모가 캔버스로 바뀌는 순간
        // 앵커 기준 사각형이 캔버스 전체로 커져 크기와 위치가 한꺼번에 튄다.
        // 드래그 중에는 중앙 점앵커로 고정하고 원래 크기를 유지해 커서와 어긋나지 않게 한다.
        Vector2 draggedSize = _rect.rect.size;

        _rect.SetParent(_dragRoot, true);
        _rect.SetAsLastSibling();
        _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.sizeDelta = draggedSize;

        // 항목의 가운데를 커서에 붙인다. anchoredPosition은 pivot 지점을 옮기므로 pivot과 중앙의 차이를 뺀다.
        _dragOffset = -Vector2.Scale(new Vector2(0.5f, 0.5f) - _rect.pivot, draggedSize);

        _dragEventCamera = ResolveEventCamera(eventData);
        MoveToPointer(eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        _dragEventCamera = ResolveEventCamera(eventData);
        MoveToPointer(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _canvasGroup.blocksRaycasts = true;
        _canvasGroup.alpha = 1f;
        _context.RefreshItemPositions();
    }

    public void OnDrop(PointerEventData eventData)
    {
        UIDraggableItem dragged = eventData.pointerDrag?.GetComponent<UIDraggableItem>();
        if (dragged != null && dragged != this)
        {
            _context.DropOnItem(this, dragged);
        }
    }

    private void MoveToPointer(Vector2 screenPosition)
    {
        if (TryGetPointerLocalPosition(screenPosition, out Vector2 pointerPosition))
        {
            _rect.anchoredPosition = pointerPosition + _dragOffset;
        }
    }

    // 빌드 해상도와 CanvasScaler 배율에 맞춰 포인터를 루트 캔버스의 로컬 좌표로 변환합니다.
    private bool TryGetPointerLocalPosition(Vector2 screenPosition, out Vector2 localPosition)
    {
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _dragRoot,
            screenPosition,
            _dragEventCamera,
            out localPosition);
    }

    private Camera ResolveEventCamera(PointerEventData eventData)
    {
        return _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : _rootCanvas.worldCamera ?? eventData.pressEventCamera;
    }
}

[Preserve]
public class UIDropSlot : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    private IUIDragDropContext _context;
    private int _slotIndex;
    private Image _background;
    private TMP_Text _placeholder;
    private Color _normalColor;
    private bool _isOccupied;

    protected void InitializeSlot(
        IUIDragDropContext context,
        int slotIndex,
        Image background,
        TMP_Text placeholder)
    {
        _context = context;
        _slotIndex = slotIndex;
        _background = background;
        _placeholder = placeholder;
        _normalColor = background.color;
    }

    public void OnDrop(PointerEventData eventData)
    {
        UIDraggableItem item = eventData.pointerDrag?.GetComponent<UIDraggableItem>();
        if (item != null)
        {
            _context.DropOnSlot(item, _slotIndex);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (eventData.pointerDrag?.GetComponent<UIDraggableItem>() != null)
        {
            _background.color = new Color(0.2f, 0.75f, 0.58f, 0.65f);
        }
    }

    public void OnPointerExit(PointerEventData eventData) => RestoreColor();

    public void SetOccupied(bool occupied)
    {
        _isOccupied = occupied;
        if (_placeholder != null)
        {
            _placeholder.gameObject.SetActive(!occupied);
        }

        RestoreColor();
    }

    public void ShowError()
    {
        if (!_isOccupied)
        {
            _background.color = new Color(0.72f, 0.12f, 0.15f, 0.7f);
        }
    }

    private void RestoreColor()
    {
        _background.color = _isOccupied
            ? new Color(_normalColor.r, _normalColor.g, _normalColor.b, 0.08f)
            : _normalColor;
    }
}

[Preserve]
public class UIDropPool : MonoBehaviour, IDropHandler
{
    private IUIDragDropContext _context;

    protected void InitializePool(IUIDragDropContext context) => _context = context;

    public void OnDrop(PointerEventData eventData)
    {
        UIDraggableItem item = eventData.pointerDrag?.GetComponent<UIDraggableItem>();
        if (item != null)
        {
            _context.ReturnToPool(item);
        }
    }
}
