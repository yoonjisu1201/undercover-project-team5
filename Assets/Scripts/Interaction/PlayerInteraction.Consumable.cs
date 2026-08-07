using Unity.Netcode;

// 조준 대상 없이 E키로 사용하는 소모품의 공통 흐름을 처리한다.
public partial class PlayerInteraction
{
    // 선택한 아이템이 지원되는 소모품이면 서버에 사용을 요청한다.
    private bool TryUseSelectedConsumable()
    {
        // 선택한 아이템이 없으면 소모품으로 처리하지 않고 다음 E 입력 흐름을 계속한다.
        if (_inventory == null || !_inventory.TryGetSelectedItem(out string itemId))
        {
            return false;
        }

        // 소모품 식별만 공통으로 처리하고 실제 효과는 아이템별 서버 메서드에 맡긴다.
        switch (itemId)
        {
            case EnergyBarItemId:
                RequestUseConsumableRpc(itemId);
                return true;

            default:
                return false;
        }
    }

    // 클라이언트가 요청한 소모품을 서버에서 다시 검증하고 해당 효과를 실행한다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestUseConsumableRpc(string itemId)
    {
        // 요청 이후 선택 아이템이 바뀌었거나 요청한 아이템과 다르면 사용하지 않는다.
        if (_inventory == null ||
            !_inventory.TryGetSelectedItem(out string selectedItemId) ||
            selectedItemId != itemId)
        {
            return;
        }

        // 검증된 아이템 ID에 해당하는 효과만 서버에서 실행한다.
        switch (itemId)
        {
            case EnergyBarItemId:
                UseEnergyBarOnServer();
                return;
        }
    }
}
