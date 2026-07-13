using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    private const int InventorySize = 4;

    [SerializeField] private InventorySlot[] _slots;

    private int _selectedIndex = -1;

    public InventorySlot[] Slots => _slots;
    public int SelectedIndex => _selectedIndex;
    public bool HasAnyItem => FindFirstOccupiedSlot() >= 0;
    public bool IsFull => FindEmptySlot() < 0;

    public event Action InventoryChanged;

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
        if (itemId == null)
            return false;

        bool wasEmpty = !HasAnyItem;
        int emptyIndex = FindEmptySlot();

        if (emptyIndex < 0)
        {
            Debug.Log("인벤토리가 가득 찼습니다.");
            return false;
        }

        _slots[emptyIndex].Set(itemId);

        if (wasEmpty)
            _selectedIndex = emptyIndex;

        InventoryChanged?.Invoke();
        Debug.Log($"Slot{emptyIndex + 1}에 '{itemId}' 추가");
        return true;
    }

    public void SelectSlot(int index)
    {
        if (index < 0 || index >= InventorySize)
            return;

        _selectedIndex = index;
        InventoryChanged?.Invoke();
    }

    public bool TryGetSelectedItem(out string itemId)
    {
        itemId = null;

        if (_selectedIndex < 0 || _selectedIndex >= InventorySize)
        {
            return false;
        }

        InventorySlot selectedSlot = _slots[_selectedIndex];

        if (selectedSlot.IsEmpty)
            return false;

        itemId = selectedSlot.ItemId;
        return true;
    }

    public bool RemoveSelectedItem()
    {
        if (_selectedIndex < 0 || _selectedIndex >= InventorySize)
            return false;

        InventorySlot selectedSlot = _slots[_selectedIndex];

        if (selectedSlot.IsEmpty)
            return false;

        selectedSlot.Clear();
        InventoryChanged?.Invoke();
        Debug.Log($"Slot{_selectedIndex + 1}에서 아이템 제거");
        return true;
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

    private int FindNextOccupiedSlot(int startIndex)
    {
        for (int offset = 1; offset <= _slots.Length; offset++)
        {
            int index = (startIndex + offset) % _slots.Length;

            if (!_slots[index].IsEmpty)
                return index;
        }
        return -1;
    }
}
