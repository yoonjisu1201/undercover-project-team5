using UnityEngine;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    [SerializeField] private PlayerInventory _inventory;
    [SerializeField] private Image[] _itemIcons;
    [SerializeField] private RectTransform _selectionOutline;
    [SerializeField] private ItemCatalog _itemCatalog;
    [SerializeField] private InteractionPromptUI _promptUI;
    private GameObject[] _slots;    // 인벤토리 슬롯 UI 오브젝트 배열

    private void Awake()
    {
        _slots = new GameObject[4];
        _itemIcons = new Image[4];

        if (_itemCatalog == null)
        {
            _itemCatalog = FindFirstObjectByType<ItemCatalog>();
        }

        if (_promptUI == null)
        {
            _promptUI = FindFirstObjectByType<InteractionPromptUI>(FindObjectsInactive.Include);
        }

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
        if (_inventory != null)
        {
            _inventory.OnInventoryChanged += Refresh;
            _inventory.OnSlotSelected += HandleSlotSelected;
        }

        Refresh();
    }

    private void OnDisable()
    {
        if (_inventory != null)
        {
            _inventory.OnInventoryChanged -= Refresh;
            _inventory.OnSlotSelected -= HandleSlotSelected;
        }
    }

    public void BindInventory(PlayerInventory inventory)    // 인벤토리 UI에 플레이어 인벤토리 연결
    {
        if (_inventory == inventory)
        {
            return;
        }

        if (_inventory != null)
        {
            _inventory.OnInventoryChanged -= Refresh;
            _inventory.OnSlotSelected -= HandleSlotSelected;
        }

        _inventory = inventory;

        if (_inventory != null && isActiveAndEnabled)
        {
            _inventory.OnInventoryChanged += Refresh;
            _inventory.OnSlotSelected += HandleSlotSelected;
        }

        Refresh();
    }

    // 슬롯 선택 시(1~4번 입력 또는 스크롤, PlayerInventory가 처리) 현재 손에 든 아이템 이름을
    // 상호작용 안내 위치에 잠시 표시한다.
    private void HandleSlotSelected(int index)
    {
        if (index < 0 || index >= _inventory.Slots.Count)
        {
            return;
        }

        InventorySlot slot = _inventory.Slots[index];
        if (slot.IsEmpty)
        {
            return;
        }

        string displayName = slot.ItemId.ToString();
        if (_itemCatalog != null && _itemCatalog.TryGet(slot.ItemId, out ItemData itemData))
        {
            displayName = itemData.DisplayName;
        }

        // 상호작용 가능한 대상을 보고 있을 때는 낮은 우선순위의 선택 아이템 안내를 표시하지 않는다.
        if (_promptUI != null && _promptUI.HasActiveInteractionPrompt)
        {
            return;
        }

        _promptUI?.ShowTemporaryPrompt(displayName);
    }

    private void Refresh()
    {
        if (_inventory == null)
            return;

        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i]?.SetActive(true);

            InventorySlot inventorySlot = _inventory.Slots[i];
            bool hasItem = !inventorySlot.IsEmpty;

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

        // 아이템이 인벤토리에 하나도 없으면 선택 테두리를 숨기기 (SelectedIndex는 항상 0 이상)
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
}
