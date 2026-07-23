using Unity.Netcode;
using UnityEngine;

public class PickupItem : InteractableBase
{
    [SerializeField] private ItemData _itemData;

    public string ItemId => _itemData != null ? _itemData.ItemId : null;
    public override string InteractionText => _itemData != null ? $"{_itemData.DisplayName} 줍기" : "줍기";
    public override bool CanInteract => Time.time >= _interactionBlockedUntil;   // 상호작용 가능 여부
    private float _interactionBlockedUntil;

    //--- 런타임에 생성된 픽업 아이템의 고유 데이터 설정 ---//
    public void Configure(ItemData itemData)
    {
        _itemData = itemData;
    }

    public void BlockInteraction(float duration)    // duration초 동안 상호작용 차단
    {
        _interactionBlockedUntil = Time.time + Mathf.Max(0f, duration);
        SetOutline(false);
    }

    public override void Interact(GameObject interactor)
    {
        if (!CanInteract || _itemData == null)
        {
            return;
        }

        if (!IsSpawned)
        {
            Debug.LogWarning($"'{name}'이 NetworkObject로 스폰되지 않았습니다.");
            return;
        }

        RequestPickupRpc();
    }

    //--- 서버에서 아이템 줍기 요청 처리 Rpc 관련 코드 ---//
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestPickupRpc(RpcParams rpcParams = default)
    {
        if (!IsSpawned || _itemData == null)
        {
            return;
        }

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient senderClient))
        {
            return;
        }

        NetworkObject playerObject = senderClient.PlayerObject;

        if (playerObject == null)
        {
            return;
        }

        SphereCollider interactionCollider = playerObject.GetComponent<SphereCollider>();

        if (interactionCollider == null)
        {
            return;
        }

        if (!IsOverlappingInteractionCollider(interactionCollider))
        {
            return;
        }

        PlayerInventory inventory = playerObject.GetComponent<PlayerInventory>();

        if (inventory == null)
        {
            return;
        }

        // 서버에서 인벤토리 공간을 확인하고 아이템을 추가
        if (!inventory.TryAddItemOnServer(_itemData.ItemId))
        {
            return;
        }

        // 모든 클라이언트에서 아이템 제거
        NetworkObject.Despawn();
    }

    private bool IsOverlappingInteractionCollider(SphereCollider interactionCollider)
    {
        Collider[] itemColliders = GetComponentsInChildren<Collider>();

        foreach (Collider itemCollider in itemColliders)
        {
            if (!itemCollider.enabled || itemCollider == interactionCollider)
            {
                continue;
            }

            if (Physics.ComputePenetration(
                    interactionCollider,
                    interactionCollider.transform.position,
                    interactionCollider.transform.rotation,
                    itemCollider,
                    itemCollider.transform.position,
                    itemCollider.transform.rotation,
                    out _,
                    out _))
            {
                return true;
            }
        }

        return false;
    }
}
