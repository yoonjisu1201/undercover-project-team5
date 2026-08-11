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

        // 서버 왕복 없이 지금 이 클라이언트에서 바로 재생한다 - item.ItemData는 로컬에서 이미 들고 있는 참조라
        // 디스폰 이후를 신경 쓸 필요가 없다. 서버가 나중에 CanUse 재검증에서 막더라도 소리는 이미 난 뒤다.
        if (_audioSource != null && item.ItemData != null && item.ItemData.AudioClip != null)
        {
            _audioSource.PlayOneShot(item.ItemData.AudioClip);
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

        string completedMessage = usable.UseCompletedMessage;

        usable.Use(gameObject, _inventory, selectedIndex);

        HandleItemUsedOwnerRpc(completedMessage, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void HandleItemUsedOwnerRpc(string message, RpcParams rpcParams = default)
    {
        _promptUI?.ShowTemporaryPrompt(message);
    }
}
