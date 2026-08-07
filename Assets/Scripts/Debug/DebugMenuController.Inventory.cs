using Unity.Netcode;
using UnityEngine;

// 로컬 플레이어의 디버그 인벤토리 명령을 처리합니다.
public sealed partial class DebugMenuController
{
    private const int DebugShopCreditAmount = 10000;
    private const string BeaconTrackerItemId = "BeaconTracker";
    private const string AlienCaptureToolItemId = "AlienCaptureGun";
    private const string AlienShotgunItemId = "AlienShotgun";

    public void OnClearInventoryClick() => RequestInventoryCommand(null, "인벤토리를 비웠습니다.");
    public void OnAddShopCreditsClick() => RequestShopCreditsCommand();
    public void OnAddBeaconTrackerClick() =>
        RequestInventoryCommand(BeaconTrackerItemId, "위치추적기 1개 추가를 요청했습니다.");
    public void OnAddAlienCaptureToolClick() =>
        RequestInventoryCommand(AlienCaptureToolItemId, "외계인 검거 도구 1개 추가를 요청했습니다.");
    public void OnAddAlienShotgunClick() =>
        RequestInventoryCommand(AlienShotgunItemId, "콩알탄 샷건 1개 추가를 요청했습니다.");

    // null 아이템 ID는 비우기, 그 외 ID는 한 개 추가 요청으로 처리합니다.
    private void RequestInventoryCommand(string itemId, string status)
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestInventoryCommandRpc(itemId);
        ShowStatus(status);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    // 요청자를 찾아 인벤토리를 비우거나 지정 아이템 한 개를 추가합니다.
    private void RequestInventoryCommandRpc(string itemId, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
            client.PlayerObject == null ||
            !client.PlayerObject.TryGetComponent(out Player player) ||
            player.PlayerInventory == null)
        {
            return;
        }

        if (itemId == null)
        {
            player.PlayerInventory.ClearAllItemsOnServer();
            return;
        }

        player.PlayerInventory.TryAddItemOnServer(itemId);
    }

    private void RequestShopCreditsCommand()
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestShopCreditsCommandRpc();
        ShowStatus($"상점 돈 {DebugShopCreditAmount:N0} 추가를 요청했습니다.");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestShopCreditsCommandRpc()
    {
        ShopManager shopManager = FindFirstObjectByType<ShopManager>(FindObjectsInactive.Include);
        if (shopManager == null)
        {
            Debug.LogWarning("[DebugMenu] ShopManager를 찾지 못했습니다.");
            return;
        }

        shopManager.AddCreditsOnServer(DebugShopCreditAmount);
    }
}
