using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class ItemBase : InteractableBase {
    [SerializeField] private ItemData _itemData;

    // 사용(IUsable)/투입(IInteractionApplier) 등 길게 눌러야 하는 상호작용에 공통으로 쓰는 시간.
    [SerializeField, Min(0.1f)] private float _holdDuration = 1.2f;

    // 프리팹 하나를 여러 ItemData가 공유하는 경우(예: Clue)가 있어서, 런타임에 주입된 종류를
    // 모든 클라이언트가 알 수 있도록 별도로 동기화한다.
    private readonly NetworkVariable<ItemType> _networkItemId =
        new(ItemType.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 인벤토리 안에 들어가 있는 동안 true. 월드에 놓여 있으면 false.
    private readonly NetworkVariable<bool> _isStored =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 비활성화 시에 렌더링 및 Collider 모두 끄기 위해 저장할 것
    private Renderer[] _renderers;
    private Collider[] _itemColliders;
    private ItemRigidbodySetter _rigidBodySetter;
    private NetworkTransform _networkTransform;

    public ItemData ItemData => _itemData;
    public ItemType ItemId => _itemData != null ? _itemData.ItemId : ItemType.None;
    public bool IsStored => _isStored.Value;
    public float HoldDuration => _holdDuration;

    public override string InteractionText => _itemData != null ? $"{_itemData.DisplayName} 줍기" : "줍기";

    protected override void Awake()
    {
        base.Awake();
        _renderers = GetComponentsInChildren<Renderer>(true);
        _itemColliders = GetComponentsInChildren<Collider>(true);
        _rigidBodySetter = GetComponent<ItemRigidbodySetter>();
        _networkTransform = GetComponent<NetworkTransform>();
    }

    public override bool CanInteract(GameObject interactor)
    {
        if (_itemData == null || IsStored)
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
    public void Configure(ItemData itemData) {
        _itemData = itemData;

        // NetworkVariable은 NetworkObject가 스폰된 뒤 서버에서만 변경한다.
        if (IsSpawned && IsServer)
        {
            _networkItemId.Value = itemData.ItemId;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _networkItemId.OnValueChanged += HandleNetworkItemIdChanged;
        _isStored.OnValueChanged += HandleStoredChanged;

        // 스폰 전에 Configure로 저장한 데이터를 네트워크 등록 완료 후 동기화한다.
        if (IsServer && _itemData != null) {
            _networkItemId.Value = _itemData.ItemId;
        }

        ResolveItemData(_networkItemId.Value);
        
        // 늦게 들어온 클라이언트도 현재 저장된 상태를 그대로 반영해야 한다.
        ApplyStoredPresentation(_isStored.Value);
    }

    public override void OnNetworkDespawn()
    {
        _networkItemId.OnValueChanged -= HandleNetworkItemIdChanged;
        _isStored.OnValueChanged -= HandleStoredChanged;
        base.OnNetworkDespawn();
    }

    private void HandleNetworkItemIdChanged(ItemType previousId, ItemType currentId)
    {
        ResolveItemData(currentId);
    }

    private void ResolveItemData(ItemType itemId)
    {
        if (itemId == ItemType.None)
        {
            return;
        }

        if (ItemCatalog.Instance != null && ItemCatalog.Instance.TryGet(itemId, out ItemData itemData))
        {
            _itemData = itemData;
        }
    }

    private void HandleStoredChanged(bool previousValue, bool currentValue)
    {
        ApplyStoredPresentation(currentValue);

        // 방금 인벤토리에 들어간 경우(주움)만, 그리고 그걸 주운 당사자 화면에서만 반응한다.
        if (currentValue && IsCarriedByLocalOwner())
        {
            OnAdded();
        }
    }

    // 부모(carrier, 인벤토리 소유 플레이어)가 지금 이 코드를 실행 중인 로컬 피어 소유인지 확인한다.
    // 다른 플레이어가 주운 아이템의 UI 반응(단서/가이드북 열기 등)이 내 화면에 뜨지 않게 하기 위함이다.
    private bool IsCarriedByLocalOwner()
    {
        return transform.parent != null
            && transform.parent.TryGetComponent(out NetworkObject carrier)
            && carrier.IsOwner;
    }

    // 이 아이템이 인벤토리에 새로 추가됐을 때(주웠을 때) 호출된다. 하위 클래스가 오버라이드해서
    // 자기만의 UI 반응(단서/가이드북 열기 등)을 정의한다.
    protected virtual void OnAdded() { }

    // 이 아이템이 핫바에서 선택됐을 때 호출된다. 하위 클래스가 오버라이드해서 UI 반응을 정의한다.
    public virtual void OnSelected() { }

    // Renderer/Collider를 개별로 끄고 켠다. NGO가 비활성 NetworkBehaviour를 지원하지 않아서
    // GameObject 자체를 SetActive로 끄지 않는다.
    private void ApplyStoredPresentation(bool isStored)
    {
        foreach (Renderer itemRenderer in _renderers)
        {
            if (itemRenderer != null)
            {
                itemRenderer.enabled = !isStored;
            }
        }

        foreach (Collider itemCollider in _itemColliders)
        {
            if (itemCollider != null)
            {
                itemCollider.enabled = !isStored;
            }
        }

        if (isStored)
        {
            SetOutline(false);
        }
    }

    public void BlockInteraction(float duration)    // duration초 동안 상호작용 차단
    {
        _interactionBlockedUntil = Time.time + Mathf.Max(0f, duration);
        SetOutline(false);
    }

    // 서버 전용: 이 아이템을 carrier(플레이어) 밑으로 넣고 재운다. 실패하면 아무것도 바꾸지 않는다.
    // RPC는 void만 반환할 수 있어서, 성공 여부는 호출부가 IsStored로 확인한다.
    [Rpc(SendTo.Server)]
    public void TryStoreItemRpc(NetworkObjectReference carrierRef) {
        // 이미 스폰된 아이템이어야 함 || 바닥에 드롭된 상태여야 함
        if (!IsSpawned || IsStored) {
            Debug.LogError($"[ItemBase] 스폰되지 않았거나 이미 타인의 인벤토리에 존재하는 아이템입니다.");
            return;
        }

        if (!carrierRef.TryGet(out NetworkObject carrier)) {
            Debug.LogError($"[ItemBase] 존재하지 않는 대상에게 아이템을 넣으려 했습니다.");
            return;
        }

        // 아이템 주운 사람 아래로 넣기
        if (!NetworkObject.TrySetParent(carrier, worldPositionStays: false)) {
            Debug.LogError($"[ItemBase] Item -> Player SetParent에 실패했습니다.");
            return;
        }

        _rigidBodySetter?.Freeze();
        _isStored.Value = true;
    }

    // 서버 전용: 인벤토리에서 꺼내 월드에 다시 놓는다.
    [Rpc(SendTo.Server)]
    public void DropItemToWorldRpc(Vector3 position, Quaternion rotation, Vector3 initialVelocity, float blockDuration)
    {
        if (!IsStored) {
            Debug.LogError($"[ItemBase] 소지중이지 않은 아이템을 버리려 했습니다");
            return;
        }

        NetworkObject.TryRemoveParent();
        transform.SetPositionAndRotation(position, rotation);
        _isStored.Value = false;

        // 부모에서 떨어지며 로컬→월드 좌표로 전환되는 순간 발생하는 이동을 순간이동으로 처리해
        // 클라이언트 화면에서 스르륵 미끄러지는 것처럼 보이지 않게 한다.
        if (_networkTransform != null) {
            _networkTransform.Teleport(position, rotation, transform.localScale);
        }

        _rigidBodySetter?.Rearm(initialVelocity);
        BlockInteraction(blockDuration);
    }

    // 서버 전용: 소비/소모되어 완전히 사라지는 경우.
    [Rpc(SendTo.Server)]
    public void DestroyRpc() {
        
        if (!IsSpawned) {
            Debug.LogError($"[ItemBase] 스폰되지 않은 아이템을 삭제하려 했습니다.");
            return;
        }

        NetworkObject.TryRemoveParent();
        NetworkObject.Despawn(true);
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
        if (!IsSpawned || _itemData == null || IsStored) {
            Debug.LogError($"[ItmeBase] 주우려는 아이템의 상태가 주울 수 없는 상태입니다.");
            return;
        }

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        // 주우려는 유저 발견 못하면 return
        if (!NetworkManager.Singleton.ConnectedClients[senderClientId].PlayerObject.TryGetComponent<Player>(out Player player)) {
            Debug.LogError($"[ItemBase] 올바르지 않은 유저 ID입니다 : {senderClientId}");
            return;
        }

        SphereCollider interactionCollider = player.GetComponent<SphereCollider>();

        // 해당 플레이어의 Collider가 없거나, 거리가 닿지 않으면 못 줍게 한다
        if (interactionCollider == null || !IsOverlappingInteractionCollider(interactionCollider)) {
            Debug.LogError($"[ItemBase] 너무 멀어서 주울 수 없습니다.");
            return;
        }

        PlayerInventory inventory = player.GetComponent<PlayerInventory>();

        // 서버에서 인벤토리 공간을 확인하고, 되면 저장까지 한 번에 처리한다 (PickUpItemRpc 내부에서 TryStoreItemRpc 호출).
        inventory.PickUpItemRpc(new NetworkBehaviourReference(this));
    }

    // 월드에 새 인스턴스를 만들어 곧바로 인벤토리에 넣는다 (상점 소모품 구매, 디버그 지급처럼
    // "월드에 존재한 적 없이 바로 인벤토리로" 들어가는 경로용).
    public static bool TrySpawnAndAddToInventory(ItemData itemData, PlayerInventory inventory)
    {
        if (itemData == null || itemData.WorldPrefab == null || inventory == null || !inventory.IsServer)
        {
            return false;
        }

        GameObject instance = Instantiate(itemData.WorldPrefab, inventory.transform.position, itemData.WorldPrefab.transform.rotation);

        if (!instance.TryGetComponent(out ItemBase itemBase) || !instance.TryGetComponent(out NetworkObject networkObject))
        {
            Debug.LogError($"[ItemBase] '{itemData.WorldPrefab.name}' 프리팹에 ItemBase 또는 NetworkObject가 없습니다.");
            Destroy(instance);
            return false;
        }

        itemBase.Configure(itemData);
        networkObject.Spawn(destroyWithScene: true);

        inventory.PickUpItemRpc(new NetworkBehaviourReference(itemBase));

        // PickUpItemRpc는 실패해도 반환값 없이 조용히 아무것도 안 하므로, 실제로 인벤토리에
        // 들어갔는지는 부작용(IsStored)으로 확인한다 - 실패 시 새로 만든 인스턴스를 정리한다.
        if (!itemBase.IsStored)
        {
            itemBase.DestroyRpc();
            return false;
        }

        return true;
    }
}
