using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

// 플레이어 인벤토리와 미션 보관함 사이의 건전지 이동 및 목표 전력 계산을 담당한다.
public sealed partial class BreakerBatteryMission
{
    private const int MinimumTargetWatt = 100;
    private const int MaximumTargetWatt = 200;

    private static bool TryGetBatteryWatt(ItemType itemId, out int watt)
    {
        switch (itemId)
        {
            case ItemType.Battery_20: watt = 20; return true;
            case ItemType.Battery_30: watt = 30; return true;
            case ItemType.Battery_40: watt = 40; return true;
            case ItemType.Battery_50: watt = 50; return true;
            default: watt = 0; return false;
        }
    }

    private static PlayerInventory FindLocalInventory()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient?.PlayerObject != null
         && NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out PlayerInventory inventory))
        {
            return inventory;
        }

        return FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None).FirstOrDefault(candidate => candidate.IsOwner) ?? FindFirstObjectByType<PlayerInventory>();
    }

    // 실제 인벤토리에서 꺼낸 배터리들을 미션 보관함으로 이동시킨다.
    private void StageInventoryBatteries()
    {
        if (_playerInventory == null)
        {
            return;
        }

        // 실제 인벤토리에서 꺼낸 배터리들을 모두 미션 보관함으로 이동시킨다.
        _stagedBatteryItemIds.AddRange(_playerInventory.MissionItems.Select(item => item.ItemId).Where(itemId => TryGetBatteryWatt(itemId, out _)));

        MoveCarriedBatteriesToMission();
    }

    // 미션 UI는 닫아도 파괴되지 않고 재사용되므로, 다시 열 때 그 사이에 새로 주운 건전지도 보관함에 들어와야 한다.
    // 이미 전원 슬롯에 꽂아둔 배치를 잃지 않도록 그리드를 다시 만들지 않고 새 건전지만 덧붙인다.
    private void StageNewBatteries()
    {
        _playerInventory ??= FindLocalInventory();
        if (_playerInventory == null)
        {
            return;
        }

        int previousCount = _stagedBatteryItemIds.Count;
        MoveCarriedBatteriesToMission();

        if (_stagedBatteryItemIds.Count > previousCount)
        {
            AppendInventoryCells(previousCount);
        }
    }

    // 플레이어가 들고 있는 건전지를 미션 보관함으로 옮기고, 옮긴 것만 보관 목록에 기록한다.
    private void MoveCarriedBatteriesToMission()
    {
        // NetworkList<T>는 foreach는 되지만 IEnumerable<T>를 구현하지 않아 LINQ가 안 먹힌다. 직접 순회.
        List<ItemType> carriedBatteries = new();
        foreach (InventorySlot slot in _playerInventory.Slots)
        {
            if (!slot.IsEmpty && TryGetBatteryWatt(slot.ItemId, out _))
            {
                carriedBatteries.Add(slot.ItemId);
            }
        }

        foreach (ItemType itemId in carriedBatteries)
        {
            if (_playerInventory.MoveItemToMission(itemId))
            {
                _stagedBatteryItemIds.Add(itemId);
            }
        }
    }

    // 현재 슬롯에 배치된 배터리들을 모두 소모하고, 미션 보관함에서 제거한다.
    private void DiscardStoredBatteries()
    {
        _playerInventory?.DiscardMissionItems();
        _stagedBatteryItemIds.Clear();
    }

    // 현재 보유한 건전지로 만들 수 있는 조합 중 무작위로 선택한 목표 전력이다.
    // A 화면에는 수치를 표시하지 않고, HQ 계기판(C)에만 눈금으로 나타난다.
    private void SelectRandomTargetWatt()
    {
        HashSet<int>[] sumsByCount = CreatePossibleSums();
        List<int> targets = sumsByCount.Skip(1).SelectMany(sums => sums).Where(sum => sum is >= MinimumTargetWatt and <= MaximumTargetWatt).Distinct().ToList();

        _targetWatt = targets.Count > 0 ? targets[UnityEngine.Random.Range(0, targets.Count)] : MinimumTargetWatt;

        _circuitState?.SubmitTargetWatt(_targetWatt);
    }

    // 배터리 조합으로 만들 수 있는 전력 합계를 계산한다. 인벤토리에서 꺼낸 배터리들을 모두 사용하지 않아도 된다.
    private HashSet<int>[] CreatePossibleSums()
    {
        HashSet<int>[] sumsByCount = Enumerable.Range(0, _slots.Length + 1).Select(_ => new HashSet<int>()).ToArray();
        sumsByCount[0].Add(0);

        foreach (ItemType itemId in _stagedBatteryItemIds)    // 인벤토리에서 꺼낸 배터리들을 모두 사용하지 않아도 된다.
        {
            if (!TryGetBatteryWatt(itemId, out int watt))
            {
                continue;
            }

            for (int count = _slots.Length; count >= 1; count--)    // 1개부터 4개까지 배터리를 조합해 만들 수 있는 합계를 계산한다.
            {
                foreach (int sum in sumsByCount[count - 1].ToArray())
                {
                    if (sum + watt <= MaximumTargetWatt)
                    {
                        sumsByCount[count].Add(sum + watt);
                    }
                }
            }
        }

        return sumsByCount;
    }
}
