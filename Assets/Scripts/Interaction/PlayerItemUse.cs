using Unity.Netcode;
using UnityEngine;

// 선택한 아이템을 E로 사용하는 실행 흐름(서버 검증·적용, 완료 알림)을 처리한다.
// "지금 사용할 수 있는지" 판정(IUsable.CanUse, hold 여부는 ItemHoldThreshold)은 PlayerInteraction이 직접 하고,
// 여기서는 실행만 담당한다.
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

    public bool TryCompleteSelectedItemUse(out string failReason)
    {
        failReason = null;

        if (!_inventory.TryGetSelectedItemBase(out ItemBase item) || item is not IUsable usable)
        {
            return false;
        }

        if (!usable.CanUse(gameObject, out failReason))
        {
            return failReason != null;
        }

        if (_audioSource != null && item.ItemData != null && item.ItemData.AudioClip != null)
        {
            _audioSource.PlayOneShot(item.ItemData.AudioClip);
        }

        RequestUseItemRpc(new NetworkBehaviourReference(item), _inventory.SelectedIndex);
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestUseItemRpc(NetworkBehaviourReference itemRef, int selectedIndex)
    {
        // 클라이언트가 보낸 슬롯 번호와 아이템 참조를 그대로 신뢰하지 않고 서버의 현재 상태로 검증한다.
        // 첫 두 조건은 슬롯 접근 범위를 보장하고, 뒤의 두 조건은 참조가 실제 사용 가능 아이템인지 확인한다.
        if (selectedIndex < 0 ||
            selectedIndex >= _inventory.Slots.Count ||
            !itemRef.TryGet(out ItemBase item) ||
            item is not IUsable usable)
        {
            return;
        }

        // 전달된 아이템이 서버가 같은 슬롯에 보관한 바로 그 인스턴스인지 확인한다.
        // 슬롯 내용과 참조가 엇갈린 요청으로 전달된 슬롯과 다른 아이템을 사용하는 것을 막는다.
        if (!_inventory.Slots[selectedIndex].TryGetItem(out ItemBase selectedItem) || selectedItem != item)
        {
            return;
        }

        if (!usable.CanUse(gameObject, out _))
        {
            return;
        }

        // Use()가 아이템을 소모(파괴)할 수 있으므로, 참조는 그 전에 미리 만들어둔다.
        NetworkBehaviourReference itemReference = new(item);

        usable.Use(gameObject, _inventory, selectedIndex);

        HandleItemUsedOwnerRpc(
            itemReference,
            usable.UseCompletedMessage,
            RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void HandleItemUsedOwnerRpc(NetworkBehaviourReference itemRef, string message, RpcParams rpcParams = default)
    {
        if (itemRef.TryGet(out ItemBase item))
        {
            item.NotifyUseCompleted();
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            _promptUI?.ShowTemporaryPrompt(message);
        }
    }
}
