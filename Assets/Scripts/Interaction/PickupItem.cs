using Unity.Netcode;
using UnityEngine;
using Unity.Collections;

public class PickupItem : InteractableBase
{
    [SerializeField] private ItemData _itemData;

    public string ItemId => _itemData != null ? _itemData.ItemId : null;
    public override string InteractionText => _itemData != null ? $"{_itemData.DisplayName} 줍기" : "줍기";
    private readonly NetworkVariable<FixedString64Bytes> _networkItemId = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public override bool CanInteract(GameObject interactor)
    {
        if (_itemData == null)
        {
            return false;
        }

        // 상호작용 가능 대상 확인
        Role interactorRole = interactor.GetComponent<Player>().PlayerRole;
        // 상호작용 가능 시간이면서, 상호작용 역할이 제한되어있지 않은 아이템이거나, 상호작용 가능한 대상의 역할과 일치해야 함
        return Time.time >= _interactionBlockedUntil
            && (_itemData.InteractableRole == Role.None || _itemData.InteractableRole == interactorRole);
    }

    private float _interactionBlockedUntil;

    //--- 런타임에 생성된 픽업 아이템의 고유 데이터 설정 ---//
    public void Configure(ItemData itemData)
    {
        _itemData = itemData;
        _networkItemId.Value = itemData.ItemId;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ResolveItemData(_networkItemId.Value.ToString());
    }

    private void ResolveItemData(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
        {
            return;
        }

        ItemCatalog catalog = FindFirstObjectByType<ItemCatalog>();
        if (catalog != null && catalog.TryGet(itemId, out ItemData itemData))
        {
            _itemData = itemData;
        }
    }

    public void BlockInteraction(float duration)    // duration초 동안 상호작용 차단
    {
        _interactionBlockedUntil = Time.time + Mathf.Max(0f, duration);
        SetOutline(false);
    }

    public override void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor) || _itemData == null)
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

    // 상호작용 콜라이더와 아이템 콜라이더가 겹치는지 확인하는 메서드
    private bool IsOverlappingInteractionCollider(SphereCollider interactionCollider)
    {
        Collider[] itemColliders = GetComponentsInChildren<Collider>();

        foreach (Collider itemCollider in itemColliders)
        {
            if (!itemCollider.enabled || itemCollider == interactionCollider)
            {
                continue;
            }

            // Physics.ComputePenetration을 사용하여 상호작용 콜라이더와 아이템 콜라이더가 겹치는지 확인
            if (Physics.ComputePenetration(interactionCollider, interactionCollider.transform.position, interactionCollider.transform.rotation,
                    itemCollider, itemCollider.transform.position, itemCollider.transform.rotation, out _, out _))
            {
                return true;
            }
        }

        return false;
    }
}
