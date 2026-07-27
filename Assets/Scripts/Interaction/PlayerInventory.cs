using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    private const int InventorySize = 4;

    [SerializeField] private InventorySlot[] _slots;

    private readonly NetworkVariable<int> _selectedIndex =
        new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public InventorySlot[] Slots => _slots;
    public int SelectedIndex => _selectedIndex.Value;
    public bool HasAnyItem => FindFirstOccupiedSlot() >= 0;
    public bool IsFull => FindEmptySlot() < 0;

    public event Action InventoryChanged;
    public event Action<string, int> ItemAdded;

    private void Awake()
    {
        if (_slots == null || _slots.Length != InventorySize)
        {
            _slots = new InventorySlot[InventorySize];
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] ??= new InventorySlot();
        }

    }

    public bool TryAddItem(string itemId)
    {
        return TryAddItemLocally(itemId);
    }

    public bool TryAddItemOnServer(string itemId)
    {
        if (!IsServer || !TryAddItemLocally(itemId))
        {
            return false;
        }

        // 원격 플레이어는 서버 인벤토리와 소유 클라이언트 인벤토리가
        // 서로 다른 인스턴스이므로 소유 클라이언트에도 결과를 전달한다.
        if (!IsOwner)
        {
            AddItemOwnerRpc(
                itemId,
                RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }

        return true;
    }

    private bool TryAddItemLocally(string itemId)
    {
        if (itemId == null)
        {
            return false;
        }

        int emptySlotIndex = FindEmptySlot();

        if (emptySlotIndex < 0)
        {
            Debug.LogWarning("인벤토리가 가득 찼습니다.");
            return false;
        }

        _slots[emptySlotIndex].Set(itemId);
        _selectedIndex.Value = emptySlotIndex;

        InventoryChanged?.Invoke();
        ItemAdded?.Invoke(itemId, emptySlotIndex);
        Debug.Log($"Slot{emptySlotIndex + 1}에 '{itemId}' 추가");
        return true;
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void AddItemOwnerRpc(string itemId, RpcParams rpcParams = default)
    {
        TryAddItemLocally(itemId);
    }

    public void SelectSlot(int index)
    {
        if (index < 0 || index >= InventorySize)
            return;

        _selectedIndex.Value = index;
        InventoryChanged?.Invoke();
    }

    public bool TryGetSelectedItem(out string itemId)
    {
        itemId = null;

        if (_selectedIndex.Value < 0 || _selectedIndex.Value >= InventorySize)
        {
            return false;
        }

        InventorySlot selectedSlot = _slots[_selectedIndex.Value];

        if (selectedSlot.IsEmpty)
            return false;

        itemId = selectedSlot.ItemId;
        return true;
    }

    public bool RemoveSelectedItem()
    {
        return RemoveSelectedItemLocally();
    }

    public bool TryRemoveSelectedItemOnServer(string expectedItemId)
    {
        if (!IsServer)
        {
            return false;
        }

        int itemIndex = FindItemSlot(expectedItemId);

        if (itemIndex < 0)
        {
            return false;
        }

        RemoveItemAt(itemIndex);

        if (!IsOwner)
        {
            RemoveSelectedItemOwnerRpc(
                expectedItemId,
                RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }

        return true;
    }

    private bool RemoveSelectedItemLocally()
    {
        if (_selectedIndex.Value < 0 || _selectedIndex.Value >= InventorySize)
            return false;

        if (_slots[_selectedIndex.Value].IsEmpty)
            return false;

        RemoveItemAt(_selectedIndex.Value);
        return true;
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void RemoveSelectedItemOwnerRpc(string expectedItemId, RpcParams rpcParams = default)
    {
        int itemIndex = FindItemSlot(expectedItemId);

        if (itemIndex >= 0)
        {
            RemoveItemAt(itemIndex);
        }
    }

    private void RemoveItemAt(int index)
    {
        _slots[index].Clear();
        InventoryChanged?.Invoke();
        Debug.Log($"Slot{index + 1}에서 아이템 제거");
    }

    private int FindItemSlot(string itemId)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (!_slots[i].IsEmpty && _slots[i].ItemId == itemId)
                return i;
        }

        return -1;
    }

    private int FindEmptySlot()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].IsEmpty)
                return i;
        }
        return -1;
    }

    private int FindFirstOccupiedSlot()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (!_slots[i].IsEmpty)
                return i;
        }
        return -1;
    }

    public void RemoveClueItemsOnServer(string clueItemIdPrefix)
    {
        if (!IsServer)
        {
            return;
        }

        RemoveItemsByPrefixLocally(clueItemIdPrefix);

        if (!IsOwner)
        {
            RemoveClueItemsOwnerRpc(clueItemIdPrefix, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void RemoveClueItemsOwnerRpc(string clueItemIdPrefix, RpcParams rpcParams = default)
    {
        RemoveItemsByPrefixLocally(clueItemIdPrefix);
    }

    private void RemoveItemsByPrefixLocally(string itemPrefix)
    {
        bool removedAny = false;

        for (int i = 0; i < _slots.Length; i++)
        {
            InventorySlot slot = _slots[i];
            if (slot.IsEmpty || !slot.ItemId.StartsWith(itemPrefix, StringComparison.Ordinal))
            {
                continue;
            }
            slot.Clear();
            removedAny = true;
        }

        if (!removedAny)
        {
            Debug.LogWarning($"인벤토리에서 '{itemPrefix}'로 시작하는 아이템을 찾지 못했습니다.");
            return;
        }

        // 선택된 슬롯이 제거된 아이템이었는지 확인하고, 필요하면 선택을 초기화
        if (_selectedIndex.Value < 0 || _selectedIndex.Value >= _slots.Length || _slots[_selectedIndex.Value].IsEmpty)
        {
            _selectedIndex.Value = FindFirstOccupiedSlot();
        }

        InventoryChanged?.Invoke();
    }
}
