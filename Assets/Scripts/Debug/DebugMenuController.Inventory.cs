using Unity.Netcode;
using UnityEngine;

// 로컬 플레이어의 디버그 인벤토리 명령을 처리합니다.
public sealed partial class DebugMenuController
{
    private const int DebugShopCreditAmount = 10000;

    public void OnClearInventoryClick() => RequestClearInventory();
    public void OnAddShopCreditsClick() => RequestShopCreditsCommand();
    public void OnAddBeaconTrackerClick() =>
        RequestInventoryCommand(ItemType.BeaconTracker, "위치추적기 1개 추가를 요청했습니다.");
    public void OnAddAlienCaptureToolClick() =>
        RequestInventoryCommand(ItemType.AlienCaptureGun, "외계인 검거 도구 1개 추가를 요청했습니다.");
    public void OnAddAlienShotgunClick() =>
        RequestInventoryCommand(ItemType.AlienShotgun, "콩알탄 샷건 1개 추가를 요청했습니다.");

    private void RequestClearInventory()
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestClearInventoryRpc();
        ShowStatus("인벤토리를 비웠습니다.");
    }

    private void RequestInventoryCommand(ItemType itemId, string status)
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
    private void RequestClearInventoryRpc(RpcParams rpcParams = default)
    {
        if (!TryGetSenderPlayer(rpcParams, out Player player))
        {
            return;
        }

        player.PlayerInventory.ClearAllItemsOnServer();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    // 요청자를 찾아 지정 아이템 한 개를 추가합니다.
    private void RequestInventoryCommandRpc(ItemType itemId, RpcParams rpcParams = default)
    {
        if (!TryGetSenderPlayer(rpcParams, out Player player))
        {
            return;
        }

        if (ItemCatalog.Instance != null && ItemCatalog.Instance.TryGet(itemId, out ItemData itemData))
        {
            ItemBase.TrySpawnAndAddToInventory(itemData, player.PlayerInventory);
        }
    }

    private static bool TryGetSenderPlayer(RpcParams rpcParams, out Player player)
    {
        player = null;
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
            client.PlayerObject == null ||
            !client.PlayerObject.TryGetComponent(out player) ||
            player.PlayerInventory == null)
        {
            player = null;
            return false;
        }

        return true;
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
