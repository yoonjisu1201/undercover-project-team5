using System.Linq;
using Unity.Netcode;
using UnityEngine;

internal static class BreakerBatteryTypes
{
    public static ItemType GetItemType(int watt)
    {
        switch (watt)
        {
            case 20: return ItemType.Battery_20;
            case 30: return ItemType.Battery_30;
            case 40: return ItemType.Battery_40;
            case 50: return ItemType.Battery_50;
            default: return ItemType.None;
        }
    }

    public static bool TryGetWatt(ItemType itemType, out int watt)
    {
        switch (itemType)
        {
            case ItemType.Battery_20: watt = 20; return true;
            case ItemType.Battery_30: watt = 30; return true;
            case ItemType.Battery_40: watt = 40; return true;
            case ItemType.Battery_50: watt = 50; return true;
            default: watt = 0; return false;
        }
    }
}

// 플레이어 인벤토리와 미션 보관함 사이의 건전지 이동을 담당한다.
public sealed partial class BreakerBatteryMission
{
    private static PlayerInventory FindLocalInventory()
    {
        NetworkObject playerObject = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        if (playerObject != null && playerObject.TryGetComponent(out PlayerInventory inventory))
        {
            return inventory;
        }

        return FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.IsOwner)
            ?? FindFirstObjectByType<PlayerInventory>();
    }

    // 패널을 다시 열었을 때 새로 주운 건전지도 서버의 공용 보관함으로 옮긴다.
    private void RequestCarriedBatteryTransfer()
    {
        _playerInventory ??= FindLocalInventory();
        if (_playerInventory == null || _circuitState == null)
        {
            return;
        }

        _circuitState.RequestTransferInventoryBatteries(_playerInventory);
    }

    // 미션 완료 시 맡겨 둔 배터리를 모두 소모한다.
    private void DiscardStoredBatteries()
    {
        _playerInventory?.DiscardMissionItems();
    }
}
