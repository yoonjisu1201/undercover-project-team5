using Unity.Netcode;
using UnityEngine;

// 선택한 아이템을 E키로 사용하는 실행 흐름(서버 검증/적용, 완료 알림)을 처리한다.
// "쓸 수 있는지" 판정(IUsable.CanUse/RequiresHold)은 PlayerInteraction이 직접 하고, 여기서는 실행만 담당한다.
// 아이템별 효과는 IUsable을 구현한 ItemBase 서브클래스(예: EnergyBar)가 갖고 있고, 여기서는 그걸 호출만 한다.
[RequireComponent(typeof(PlayerInventory))]
public class PlayerItemUse : NetworkBehaviour
{
    private PlayerInventory _inventory;
    private AudioSource _audioSource;
    private InteractionPromptUI _promptUI;

    private void Awake()
    {
        _inventory = GetComponent<PlayerInventory>();
        _audioSource = GetComponent<AudioSource>();

        if (_inventory == null)
        {
            Debug.LogError("[PlayerItemUse] PlayerInventory가 없어 선택 아이템을 사용할 수 없습니다.", this);
        }

        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }

        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f;
    }

    public void BindInteractionPromptUI(InteractionPromptUI promptUI)
    {
        _promptUI = promptUI;
    }

    public bool TryCompleteSelectedItemUse(out string message)
    {
        message = null;

        if (!_inventory.TryGetSelectedItemBase(out ItemBase item) || item is not IUsable usable)
        {
            return false;
        }

        if (!usable.CanUse(gameObject, out message))
        {
            return message != null;
        }

        RequestUseItemRpc(new NetworkBehaviourReference(item), _inventory.SelectedIndex);
        return true;
    }

    // 선택 상태를 서버에서 다시 확인한 뒤 아이템 효과를 실행한다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestUseItemRpc(NetworkBehaviourReference itemRef, int selectedIndex)
    {
        if (selectedIndex < 0 || !itemRef.TryGet(out ItemBase item) || item is not IUsable usable)
        {
            return;
        }

        if (!usable.CanUse(gameObject, out _))
        {
            return;
        }

        // 소모(디스폰) 전에 필요한 값을 먼저 뽑아둔다 - Use()가 스스로 소모시키면 이후 item 인스턴스는 사라질 수 있다.
        ItemType usedItemId = item.ItemId;
        string completedMessage = usable.UseCompletedMessage;

        usable.Use(gameObject, _inventory, selectedIndex);

        HandleItemUsedOwnerRpc(usedItemId, completedMessage, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
    }

    // 아이템별 전용 RPC 대신 itemId 하나로 통일. 소리는 ItemData.AudioClip에서 가져온다
    // (item 인스턴스는 이미 디스폰됐을 수 있어서, 네트워크로 안 넘어가는 로컬 에셋 참조로 해결한다).
    [Rpc(SendTo.SpecifiedInParams)]
    private void HandleItemUsedOwnerRpc(ItemType itemId, string message, RpcParams rpcParams = default)
    {
        _promptUI?.ShowTemporaryPrompt(message);

        if (_audioSource != null && ItemCatalog.Instance != null
            && ItemCatalog.Instance.TryGet(itemId, out ItemData itemData) && itemData.AudioClip != null)
        {
            _audioSource.PlayOneShot(itemData.AudioClip);
        }
    }
}
