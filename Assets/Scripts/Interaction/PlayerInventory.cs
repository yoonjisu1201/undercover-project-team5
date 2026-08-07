using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    private const int InventorySize = 4;

    [SerializeField] private InventorySlot[] _slots;
    // 미션에 맡긴 아이템은 일반 인벤토리 슬롯을 차지하지 않고 플레이어가 별도로 보관한다.
    private readonly List<string> _missionItems = new();

    private readonly NetworkVariable<int> _selectedIndex =
        new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public InventorySlot[] Slots => _slots;
    public IReadOnlyList<string> MissionItems => _missionItems;
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

    // 미션에 아이템 한 개를 넘기고 일반 인벤토리 슬롯에서는 제거한다.
    public bool MoveItemToMission(string itemId)
    {
        if (string.IsNullOrEmpty(itemId) || FindItemSlot(itemId) < 0)
        {
            return false;
        }

        if (IsServer)
        {
            return MoveItemToMissionLocally(itemId);
        }

        if (!IsOwner || !MoveItemToMissionLocally(itemId))
        {
            return false;
        }

        RemoveMissionItemServerRpc(itemId);
        return true;
    }

    [Rpc(SendTo.Server)]
    private void RemoveMissionItemServerRpc(string itemId)
    {
        MoveItemToMissionLocally(itemId);
    }

    // 미션 종료 시 보관함의 모든 아이템을 인벤토리로 반환하지 않고 폐기한다.
    public void DiscardMissionItems()
    {
        if (_missionItems.Count == 0)
        {
            return;
        }

        _missionItems.Clear();
        if (!IsServer)
        {
            if (!IsOwner)
            {
                return;
            }

            DiscardMissionItemsServerRpc();
        }
    }

    [Rpc(SendTo.Server)]
    private void DiscardMissionItemsServerRpc()
    {
        _missionItems.Clear();
    }

    private bool MoveItemToMissionLocally(string itemId)
    {
        if (!RemoveItemByIdLocally(itemId))
        {
            return false;
        }

        _missionItems.Add(itemId);
        return true;
    }

    private bool RemoveItemByIdLocally(string itemId)
    {
        int itemIndex = FindItemSlot(itemId);
        if (itemIndex < 0)
        {
            return false;
        }

        RemoveItemAt(itemIndex);
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

    public void ClearAllItemsOnServer()
    {
        if (!IsServer)
        {
            return;
        }

        ClearAllItemsLocally();

        if (!IsOwner)
        {
            ClearAllItemsOwnerRpc(RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void ClearAllItemsOwnerRpc(RpcParams rpcParams = default)
    {
        ClearAllItemsLocally();
    }

    private void ClearAllItemsLocally()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i].Clear();
        }

        _missionItems.Clear();

        // _selectedIndex는 Owner만 쓸 수 있는 NetworkVariable이라, 서버가 남의 인벤토리를 정리할 때는
        // 여기서 건드리지 않고 오너 클라이언트에서 실행되는 호출(ClearAllItemsOwnerRpc)에서만 반영한다.
        if (IsOwner)
        {
            _selectedIndex.Value = -1;
        }

        InventoryChanged?.Invoke();
    }
}
