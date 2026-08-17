using UnityEngine;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    [SerializeField] private Image[] _itemIcons;
    [SerializeField] private RectTransform _selectionOutline;
    [SerializeField] private GameObject _shotgunCrosshair;
    [SerializeField] private ItemCatalog _itemCatalog;
    private GameObject[] _slots;    // 인벤토리 슬롯 UI 오브젝트 배열

    private void Awake()
    {
        _slots = new GameObject[4];
        _itemIcons = new Image[4];

        if (_itemCatalog == null)
        {
            _itemCatalog = FindFirstObjectByType<ItemCatalog>();
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

    public void Refresh(PlayerInventory inventory)
    {
        if (inventory == null)
            return;

        bool showShotgunCrosshair =
            inventory.TryGetSelectedItemId(out ItemType selectedItemId) &&
            selectedItemId == ItemType.AlienShotgun;
        _shotgunCrosshair.SetActive(showShotgunCrosshair);

        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i]?.SetActive(true);

            InventorySlot inventorySlot = inventory.Slots[i];
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

        // 아이템이 하나도 없거나, 선택이 잠긴 상태(NoSelectionIndex, 카트 끄는 중 등)면 선택 테두리를 숨기기
        bool showSelection = inventory.HasAnyItem && inventory.SelectedIndex >= 0 && inventory.SelectedIndex < _slots.Length;

        if (_selectionOutline == null)
            return;

        _selectionOutline.gameObject.SetActive(showSelection);

        if (showSelection)  // 선택한 슬롯의 위치를 찾아서 선택 테두리를 그 위치로 옮기기
        {
            RectTransform selectedSlot = (RectTransform)_slots[inventory.SelectedIndex].transform;

            _selectionOutline.anchoredPosition = selectedSlot.anchoredPosition;
        }
    }
}
