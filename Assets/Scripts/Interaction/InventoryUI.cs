using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    [SerializeField] private PlayerInventory _inventory;
    [SerializeField] private Image[] _itemIcons;
    [SerializeField] private RectTransform _selectionOutline;
    [SerializeField] private ItemCatalog _itemCatalog;
    [SerializeField] private TMP_Text _interactionPromptText;
    private GameObject[] _slots;    // 인벤토리 슬롯 UI 오브젝트 배열

    private CustomInputActions _actions;

    private void Awake()
    {
        _actions = new CustomInputActions();
        _slots = new GameObject[4];
        _itemIcons = new Image[4];

        if (_interactionPromptText == null)
        {
            _interactionPromptText = transform.Find("InteractionPrompt")?.GetComponent<TMP_Text>();
        }

        SetInteractionPrompt(null);

        for (int i = 0; i < _slots.Length; i++)
        {
            Transform slot = transform.Find($"InventoryPanel/Slot{i + 1}");
            _slots[i] = slot != null ? slot.gameObject : null;

            if (_slots[i] != null)
            {
                _slots[i].SetActive(true);
                _itemIcons[i] = _slots[i].transform
                    .Find("ItemIcon")?.GetComponent<Image>();
            }
        }
    }

    private void OnEnable()
    {
        _actions ??= new CustomInputActions();
        _actions.Enable();

        if (_inventory != null)
        {
            _inventory.InventoryChanged += Refresh;
        }

        Refresh();
    }

    private void OnDisable()
    {
        _actions?.Disable();

        if (_inventory != null)
        {
            _inventory.InventoryChanged -= Refresh;
        }
    }

    private void Update()
    {
        if (_actions.Player.Slot1.WasPressedThisFrame())
            _inventory.SelectSlot(0);

        if (_actions.Player.Slot2.WasPressedThisFrame())
            _inventory.SelectSlot(1);

        if (_actions.Player.Slot3.WasPressedThisFrame())
            _inventory.SelectSlot(2);

        if (_actions.Player.Slot4.WasPressedThisFrame())
            _inventory.SelectSlot(3);
    }

    public void SetInteractionPrompt(string interactionText)    // 상호작용 프롬프트 텍스트 설정
    {
        if (_interactionPromptText == null)
        {
            return;
        }

        bool isVisible = !string.IsNullOrWhiteSpace(interactionText);
        if (isVisible)
        {
            _interactionPromptText.text = $"{interactionText} : [E]";
        }

        _interactionPromptText.gameObject.SetActive(isVisible);
    }

    private void Refresh()
    {
        if (_inventory == null)
            return;

        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i]?.SetActive(true);

            InventorySlot inventorySlot = _inventory.Slots[i];
            bool hasItem = inventorySlot != null && !inventorySlot.IsEmpty;

            if (_itemIcons[i] == null)
                continue;


            _itemIcons[i].sprite = null;
            _itemIcons[i].enabled = false;

            if (hasItem && _itemCatalog != null && _itemCatalog.TryGet(inventorySlot.ItemId, out ItemData itemData))
            {
                _itemIcons[i].sprite = itemData.Icon;
                _itemIcons[i].enabled = true;
            }
        }

        // 아이템이 인벤토리에 없으면 선택 테두리를 숨기기 (없으면 SelectedIndex 는 -1)
        bool showSelection = _inventory.HasAnyItem && _inventory.SelectedIndex >= 0 && _inventory.SelectedIndex < _slots.Length;

        if (_selectionOutline == null)
            return;

        _selectionOutline.gameObject.SetActive(showSelection);

        if (showSelection)  // 선택한 슬롯의 위치를 찾아서 선택 테두리를 그 위치로 옮기기
        {
            RectTransform selectedSlot = (RectTransform)_slots[_inventory.SelectedIndex].transform;

            _selectionOutline.anchoredPosition = selectedSlot.anchoredPosition;
        }
    }

    public void Interact(GameObject interactor)
    {
        throw new System.NotImplementedException();
    }
}
