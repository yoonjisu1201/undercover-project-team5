using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public interface IUIDragDropContext
{
    void DropOnSlot(UIDraggableItem item, int slotIndex);
    void DropOnItem(UIDraggableItem target, UIDraggableItem dragged);
    void ReturnToPool(UIDraggableItem item);
    void RefreshItemPositions();
}

public class UIDraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    private IUIDragDropContext _context;
    private RectTransform _rect;
    private RectTransform _dragRoot;
    private CanvasGroup _canvasGroup;
    private Vector2 _dragOffset;

    public int CurrentSlotIndex { get; set; } = -1;
    public object Payload { get; private set; }

    protected void InitializeDrag(IUIDragDropContext context, object payload)
    {
        _context = context;
        Payload = payload;
        _rect = (RectTransform)transform;
        _dragRoot = (RectTransform)GetComponentInParent<Canvas>().rootCanvas.transform;
        _canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.alpha = 0.82f;
        _rect.SetParent(_dragRoot, true);
        _rect.SetAsLastSibling();

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _dragRoot, eventData.position, eventData.pressEventCamera, out Vector2 pointerPosition))
        {
            _dragOffset = _rect.anchoredPosition - pointerPosition;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _dragRoot, eventData.position, eventData.pressEventCamera, out Vector2 point))
        {
            _rect.anchoredPosition = point + _dragOffset;
        }
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
}

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
