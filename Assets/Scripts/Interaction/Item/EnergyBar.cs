using UnityEngine;

// IUsable의 첫 구현체. 행동이 PlayerItemUse의 switch가 아니라 여기 있다.
public class EnergyBar : ItemBase, IUsable
{
    [SerializeField, Min(0f)] private float _healAmount = 30f;

    public string UseText => "에너지바 마시기";
    public string UseCompletedMessage => "에너지바 사용";

    public bool CanUse(GameObject user, out string failReason)
    {
        failReason = null;

        if (!user.TryGetComponent(out PlayerHealth health))
        {
            return false;
        }

        if (health.IsDowned)
        {
            return false;
        }

        if (health.CurrentHp >= health.MaxHp)
        {
            failReason = "HP가 가득 차 있습니다.";
            return false;
        }

        return true;
    }

    public void Use(GameObject user, PlayerInventory inventory, int selectedIndex)
    {
        if (user.TryGetComponent(out PlayerHealth health))
        {
            health.RestoreHealth(_healAmount);
        }

        inventory.TryRemoveSelectedItemOnServer(ItemId, selectedIndex);
    }
}
