using Unity.Netcode;

// 선택한 아이템을 E키로 사용하는 흐름과 아이템별 효과를 처리한다.
public class UsableItem : NetworkBehaviour, IUsableItem
{
    private PlayerInventory _inventory;
    private PlayerHealth _health;

    private void Awake()
    {
        _inventory = GetComponent<PlayerInventory>();
        _health = GetComponent<PlayerHealth>();
    }

    // 선택한 아이템이 사용 가능한 아이템이면 서버에 사용을 요청한다.
    public bool TryUseSelectedItem()
    {
        if (!_inventory.TryGetSelectedItem(out string itemId))
        {
            return false;
        }

        switch (itemId)
        {
            case EnergyBarItemId:
                RequestUseItemRpc(itemId);
                return true;

            default:
                return false;
        }
    }

    // 선택 상태를 서버에서 다시 확인한 뒤 아이템 효과를 실행한다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestUseItemRpc(string itemId)
    {
        if (!_inventory.TryGetSelectedItem(out string selectedItemId) || selectedItemId != itemId)
        {
            return;
        }

        switch (itemId)
        {
            case EnergyBarItemId:
                UseEnergyBarOnServer();
                return;
        }
    }

    // EnergyBar --------------------------------------------------

    private const string EnergyBarItemId = "EnergyBar";
    private const float EnergyBarHealAmount = 30f;

    private void UseEnergyBarOnServer()
    {
        if (_health.IsDowned || _health.CurrentHp >= _health.MaxHp)
        {
            return;
        }

        if (!_inventory.TryRemoveSelectedItemOnServer(EnergyBarItemId))
        {
            return;
        }

        _health.RestoreHealth(EnergyBarHealAmount);
    }
}
