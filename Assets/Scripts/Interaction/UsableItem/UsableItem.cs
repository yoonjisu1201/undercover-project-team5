using Unity.Netcode;
using UnityEngine;

// 선택한 아이템을 E키로 사용하는 흐름과 아이템별 효과를 처리한다.
[RequireComponent(typeof(PlayerInventory), typeof(PlayerHealth))]
public class UsableItem : NetworkBehaviour, IUsableItem
{
    [SerializeField] private AudioClip _eatingClip;
    [SerializeField, Range(0f, 1f)] private float _eatingVolume = 1f;

    private PlayerInventory _inventory;
    private PlayerHealth _health;
    private AudioSource _audioSource;
    private InventoryUI _inventoryUI;

    private void Awake()
    {
        _inventory = GetComponent<PlayerInventory>();
        _health = GetComponent<PlayerHealth>();
        _audioSource = GetComponent<AudioSource>();

        if (_inventory == null)
        {
            Debug.LogError("[UsableItem] PlayerInventory가 없어 선택 아이템을 사용할 수 없습니다.", this);
        }

        if (_health == null)
        {
            Debug.LogError("[UsableItem] PlayerHealth가 없어 에너지바 회복 처리를 할 수 없습니다.", this);
        }

        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }

        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f;
    }

    public void BindInventoryUI(InventoryUI inventoryUI)
    {
        _inventoryUI = inventoryUI;
    }

    // 선택한 아이템이 사용 입력을 처리할 수 있는지 확인한다.
    public bool TryGetSelectedItemUse(out string message, out bool requiresHold)
    {
        message = null;
        requiresHold = false;

        if (!_inventory.TryGetSelectedItem(out string itemId))
        {
            return false;
        }

        switch (itemId)
        {
            case EnergyBarItemId:
                if (_health.CurrentHp >= _health.MaxHp)
                {
                    message = "HP가 가득 차 있습니다.";
                    return true;
                }

                requiresHold = true;
                return true;

            default:
                return false;
        }
    }

    public bool TryCompleteSelectedItemUse(out string message)
    {
        message = null;

        if (!_inventory.TryGetSelectedItem(out string itemId))
        {
            return false;
        }

        switch (itemId)
        {
            case EnergyBarItemId:
                if (_health.CurrentHp >= _health.MaxHp)
                {
                    message = "HP가 가득 차 있습니다.";
                    return true;
                }

                RequestUseItemRpc(itemId, _inventory.SelectedIndex);
                return true;

            default:
                return false;
        }
    }

    // 선택 상태를 서버에서 다시 확인한 뒤 아이템 효과를 실행한다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestUseItemRpc(string itemId, int selectedIndex)
    {
        if (selectedIndex < 0)
        {
            return;
        }

        switch (itemId)
        {
            case EnergyBarItemId:
                UseEnergyBarOnServer(selectedIndex);
                return;
        }
    }

    // EnergyBar --------------------------------------------------

    private const string EnergyBarItemId = "EnergyBar";
    private const float EnergyBarHealAmount = 30f;

    private void UseEnergyBarOnServer(int selectedIndex)
    {
        if (_health.IsDowned || _health.CurrentHp >= _health.MaxHp)
        {
            return;
        }

        if (!_inventory.TryRemoveSelectedItemOnServer(EnergyBarItemId, selectedIndex))
        {
            return;
        }

        _health.RestoreHealth(EnergyBarHealAmount);
        HandleEnergyBarUsedOwnerRpc(RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void HandleEnergyBarUsedOwnerRpc(RpcParams rpcParams = default)
    {
        _inventoryUI?.ShowTemporaryPrompt("에너지바 사용");

        if (_audioSource != null && _eatingClip != null)
        {
            _audioSource.PlayOneShot(_eatingClip, _eatingVolume);
        }
    }
}
