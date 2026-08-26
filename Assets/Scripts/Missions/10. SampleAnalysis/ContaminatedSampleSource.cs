using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;

// 현장에 놓인 오염 흔적이다. E를 길게 눌러 채취하면 인벤토리에 샘플 아이템만 들어가고, 바닥의 흔적은 그대로 남는다.
// 흔적을 지우지 않는 이유는, 다른 요원이 나중에 와도 이곳에서 무슨 일이 있었는지 알 수 있어야 하기 때문이다.
public sealed class ContaminatedSampleSource : InteractableBase
{
    // 채취했을 때 인벤토리에 넣어 줄 아이템이다. 스포너가 Configure로 넣어 준다.
    [SerializeField] private ItemData _sampleItem;
    // E를 눌러 채취를 끝내기까지 걸리는 시간이다.
    [SerializeField, Min(0f)] private float _collectHoldDuration = 1.2f;

    // 이미 채취한 플레이어 목록이다. 요원마다 한 번씩 샘플을 가져갈 수 있게 서버가 관리한다.
    private readonly NetworkList<ulong> _collectorClientIds = new(readPerm: NetworkVariableReadPermission.Everyone, writePerm: NetworkVariableWritePermission.Server);

    public override string InteractionText => _sampleItem != null
        ? LocalizeInteractionText("interact_collect_sample", _sampleItem.DisplayName)
        : LocalizeInteractionText("interact_collect_sample_generic");


    // 스포너가 샘플 아이템을 지정해준다. 샘플 아이템을 설정하지 않으면 채취 불가
    public void Configure(ItemData sampleItem)
    {
        _sampleItem = sampleItem;
        if (_sampleItem == null)
        {
            Debug.LogError("[ContaminatedSampleSource] 채취 후 지급할 샘플 아이템이 없습니다.", this);
        }
    }

    // 즉시 줍기가 아니라 길게 누르게 해서, 위험한 것을 조심히 담는 느낌을 준다.
    // PlayerInteraction의 공통 길게 누르기 UI가 이 시간을 그대로 쓴다.
    public override float InteractHoldThreshold => _collectHoldDuration;

    // 역할과 상관없이, 아직 이 플레이어가 채취하지 않았으면 채취할 수 있다.
    public override bool CanInteract(GameObject interactor)
    {
        return _sampleItem != null && !HasCollected(interactor);
    }

    // 이미 채취한 사람에게는 입력 키를 띄우지 않는다. 흔적 자체는 남아 있으므로 안내만 지운다.
    public override bool ShowInteractionKeyHint(GameObject interactor) => !HasCollected(interactor);

    // 채취 기록은 클라이언트 ID로 남긴다. 조준 중인 본인이 이미 가져갔는지 클라이언트에서도 같은 목록으로 판단한다.
    private bool HasCollected(GameObject interactor)
    {
        return interactor != null
            && interactor.TryGetComponent(out NetworkObject interactorObject)
            && _collectorClientIds.Contains(interactorObject.OwnerClientId);
    }

    public override void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor))
        {
            return;
        }

        if (!IsSpawned)
        {
            Debug.LogWarning("[ContaminatedSampleSource] NetworkObject가 Spawn되지 않아 채취할 수 없습니다.", this);
            return;
        }

        RequestCollectRpc();
    }

    // 클라이언트가 보낸 채취 요청을 서버가 검증한다. 겹침을 다시 확인하는 것은 멀리서 보낸 요청을 막기 위한 것이다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestCollectRpc(RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (_sampleItem == null || _collectorClientIds.Contains(senderClientId))
        {
            return;
        }

        if (!NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client)
            || client.PlayerObject == null)
        {
            return;
        }

        SphereCollider interactionCollider = client.PlayerObject.GetComponent<SphereCollider>();
        if (interactionCollider == null || !IsOverlappingInteractionCollider(interactionCollider))
        {
            return;
        }

        // 인벤토리가 꽉 찼으면 지급에 실패하므로, 채취 표시도 남기지 않고 다시 시도할 수 있게 둔다.
        if (!client.PlayerObject.TryGetComponent(out PlayerInventory inventory)
            || !ItemBase.TrySpawnAndAddToInventory(_sampleItem, inventory))
        {
            return;
        }

        _collectorClientIds.Add(senderClientId);
    }
}
