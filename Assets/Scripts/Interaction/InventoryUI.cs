using UnityEngine;
using UnityEngine.UI;

// 인벤토리 슬롯 표시. 어떤 인벤토리를 그릴지 스스로 찾아 붙는다.
//
// 예전에는 PlayerInventory 가 이 UI 의 참조를 들고 갱신을 밀어 넣었다. 플레이어 오브젝트는 씬
// 전환에도 살아남는데 이 UI 는 씬과 함께 새로 만들어지므로, 바인딩을 한 번이라도 놓치면
// (씬 이벤트 타임아웃, 라운드 전환 등) 그 뒤의 모든 갱신이 조용히 사라졌다. 아이템을 주워도
// 화면에만 안 보이고, 다음 변경이 올 때까지 그 상태로 남는 게 그 증상이었다.
//
// 이제는 UI 가 살아 있는 인벤토리를 스스로 확인하고, 붙는 순간의 상태를 곧바로 그린다.
// 놓칠 바인딩 시점이 없어진다.
public class InventoryUI : MonoBehaviour
{
    private const int SlotCount = 4;

    [SerializeField] private Image[] _itemIcons;
    [SerializeField] private RectTransform _selectionOutline;
    [SerializeField] private ItemCatalog _itemCatalog;

    private GameObject[] _slots;    // 인벤토리 슬롯 UI 오브젝트 배열
    private PlayerInventory _boundInventory;

    // 로컬 플레이어 쪽 참조. 관전 중이 아니면 이 인벤토리를 그린다.
    private PlayerInventory _ownerInventory;
    private PlayerSpectator _spectator;

    private void Awake()
    {
        _slots = new GameObject[SlotCount];
        _itemIcons = new Image[SlotCount];

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

    private void OnDisable()
    {
        Unbind();
    }

    // 플레이어는 씬 로드보다 늦게 스폰되고, 라운드가 끝나면 파괴된 참조가 남는다. 그래서 붙을
    // 대상이 없을 때만 다시 찾는다. 살아 있으면 아무것도 하지 않는다.
    private void Update()
    {
        if (_ownerInventory == null)
        {
            foreach (Player player in Player.ActiveInstances)
            {
                if (!player.IsOwner) continue;

                _ownerInventory = player.PlayerInventory;
                _spectator = player.GetComponent<PlayerSpectator>();
                break;
            }
        }

        // 관전 중에는 보고 있는 팀원의 인벤토리를, 아니면 내 것을 그린다.
        Player target = _spectator != null ? _spectator.CurrentTarget : null;
        PlayerInventory desired = target != null ? target.PlayerInventory : _ownerInventory;

        if (desired == _boundInventory) return;

        Unbind();
        Bind(desired);
    }

    private void Bind(PlayerInventory inventory)
    {
        if (inventory == null) return;

        _boundInventory = inventory;
        _boundInventory.OnInventoryChanged += HandleInventoryChanged;

        // 붙는 순간 이미 들고 있던 것이 있을 수 있다. 다음 변경을 기다리면 그때까지 빈 칸으로 보인다.
        Refresh();
    }

    private void Unbind()
    {
        // 파괴된 인벤토리에는 구독을 뗄 수도 없고, 뗄 필요도 없다(알리는 쪽이 이미 사라졌다).
        if (_boundInventory == null) return;

        _boundInventory.OnInventoryChanged -= HandleInventoryChanged;
        _boundInventory = null;
    }

    private void HandleInventoryChanged()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (_boundInventory == null) return;

        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i]?.SetActive(true);

            if (_itemIcons[i] == null)
                continue;

            _itemIcons[i].sprite = null;
            _itemIcons[i].enabled = false;

            if (!TryGetSlot(i, out InventorySlot inventorySlot) || inventorySlot.IsEmpty)
                continue;

            if (TryGetCatalog(out ItemCatalog catalog)
                && catalog.TryGet(inventorySlot.ItemId, out ItemData itemData))
            {
                _itemIcons[i].sprite = itemData.Icon;
                _itemIcons[i].enabled = true;
            }
        }

        // 아이템이 하나도 없거나, 선택이 잠긴 상태(NoSelectionIndex, 카트 끄는 중 등)면 선택 테두리를 숨기기
        bool showSelection = _boundInventory.HasAnyItem
            && _boundInventory.SelectedIndex >= 0
            && _boundInventory.SelectedIndex < _slots.Length
            && _slots[_boundInventory.SelectedIndex] != null;

        if (_selectionOutline == null)
            return;

        _selectionOutline.gameObject.SetActive(showSelection);

        if (showSelection)  // 선택한 슬롯의 위치를 찾아서 선택 테두리를 그 위치로 옮기기
        {
            RectTransform selectedSlot = (RectTransform)_slots[_boundInventory.SelectedIndex].transform;

            _selectionOutline.anchoredPosition = selectedSlot.anchoredPosition;
        }
    }

    // 슬롯 목록은 서버가 스폰 직후에 채우고 동기화된다. 그 사이에 그리면 개수가 0 이라
    // 그냥 인덱스로 읽으면 예외가 나고, 예외가 나면 그 뒤 칸들이 통째로 갱신되지 않는다.
    private bool TryGetSlot(int index, out InventorySlot slot)
    {
        if (index >= _boundInventory.Slots.Count)
        {
            slot = default;
            return false;
        }

        slot = _boundInventory.Slots[index];
        return true;
    }

    // 카탈로그도 Awake 에서 한 번만 찾으면, 그 시점에 없으면 영구히 아이콘이 안 뜬다.
    private bool TryGetCatalog(out ItemCatalog catalog)
    {
        if (_itemCatalog == null)
        {
            _itemCatalog = FindFirstObjectByType<ItemCatalog>();
        }

        catalog = _itemCatalog;
        return catalog != null;
    }
}
