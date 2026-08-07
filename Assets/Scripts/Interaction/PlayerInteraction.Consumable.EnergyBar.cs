// 에너지 바의 사용 조건과 회복 효과를 처리한다.
public partial class PlayerInteraction
{
    // ItemData에 등록된 에너지 바의 아이템 ID.
    private const string EnergyBarItemId = "EnergyBar";

    // 에너지 바를 한 번 사용할 때 회복하는 체력.
    private const float EnergyBarHealAmount = 30f;

    // 서버에서 사용 가능 여부를 확인한 뒤 에너지 바를 차감하고 체력을 회복한다.
    private void UseEnergyBarOnServer()
    {
        // 회복할 수 없거나 회복이 필요하지 않은 상태에서는 에너지 바를 소비하지 않는다.
        if (_health == null || _health.IsDowned)
        {
            return;
        }

        // 인벤토리에서 에너지 바 차감에 성공한 경우에만 회복 효과를 적용한다.
        if (!_inventory.TryRemoveSelectedItemOnServer(EnergyBarItemId))
        {
            return;
        }

        // 최대 체력 제한을 포함한 실제 체력 변경은 PlayerHealth에 맡긴다.
        _health.RestoreHealth(EnergyBarHealAmount);
    }
}
