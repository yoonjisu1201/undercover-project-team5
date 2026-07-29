using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

// 플레이어 인벤토리와 미니게임 보관함 사이의 건전지 이동 및 목표 전력 계산을 담당한다.
public sealed partial class BreakerBatteryMiniGame
{
    private static readonly int[] SupportedWatts = { 20, 30, 40, 50 };
    private const int MinimumTargetWatt = 100;
    private const int MaximumTargetWatt = 200;

    private static bool TryGetBatteryWatt(string itemId, out int watt)
    {
        watt = 0;
        if (string.IsNullOrWhiteSpace(itemId)
            || itemId.IndexOf("battery", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        foreach (int supportedWatt in SupportedWatts)
        {
            if (itemId.Contains(supportedWatt.ToString(), StringComparison.Ordinal))
            {
                watt = supportedWatt;
                return true;
            }
        }

        return false;
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

    // 실제 인벤토리에서 꺼낸 배터리들을 미니게임 보관함으로 이동시킨다.
    private void StageInventoryBatteries()
    {
        if (_playerInventory == null)
        {
            return;
        }

        // 실제 인벤토리에서 꺼낸 배터리들을 모두 미니게임 보관함으로 이동시킨다.
        _stagedBatteryItemIds.AddRange(_playerInventory.MiniGameItems.Where(itemId => TryGetBatteryWatt(itemId, out _)));

        string[] carriedBatteries = _playerInventory.Slots.Where(slot => slot != null && !slot.IsEmpty).Select(slot => slot.ItemId)
        .Where(itemId => TryGetBatteryWatt(itemId, out _)).ToArray();

        foreach (string itemId in carriedBatteries)
        {
            if (_playerInventory.MoveItemToMiniGame(itemId))
            {
                _stagedBatteryItemIds.Add(itemId);
            }
        }
    }

    // 현재 슬롯에 배치된 배터리들을 모두 소모하고, 미니게임 보관함에서 제거한다.
    private void DiscardStoredBatteries()
    {
        _playerInventory?.DiscardMiniGameItems();
        _stagedBatteryItemIds.Clear();
    }

    // 현재 보유한 건전지로 만들 수 있는 조합 중 무작위로 선택한 목표 전력이다.
    private void SelectRandomTargetWatt()
    {
        HashSet<int>[] sumsByCount = CreatePossibleSums();
        List<int> targets = sumsByCount.Skip(1).SelectMany(sums => sums).Where(sum => sum is >= MinimumTargetWatt and <= MaximumTargetWatt).Distinct().ToList();

        _targetWatt = targets.Count > 0 ? targets[UnityEngine.Random.Range(0, targets.Count)] : MinimumTargetWatt;

        _targetValueText.text = $"{_targetWatt:000} W";
        _confirmButton.interactable = targets.Count > 0;
    }

    // 배터리 조합으로 만들 수 있는 전력 합계를 계산한다. 인벤토리에서 꺼낸 배터리들을 모두 사용하지 않아도 된다.
    private HashSet<int>[] CreatePossibleSums()
    {
        HashSet<int>[] sumsByCount = Enumerable.Range(0, _slots.Length + 1).Select(_ => new HashSet<int>()).ToArray();
        sumsByCount[0].Add(0);

        foreach (string itemId in _stagedBatteryItemIds)    // 인벤토리에서 꺼낸 배터리들을 모두 사용하지 않아도 된다.
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
