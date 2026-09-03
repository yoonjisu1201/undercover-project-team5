using Unity.Netcode;
using Unity.Netcode.Components;
using EPOOutline;
using UnityEngine;
using UnityEngine.Localization;

// 손에 들린 아이템은 LateUpdate 에서 손 앵커를 따라간다. 1인칭에서는 그 앵커가 뷰모델 손이라,
// 손을 옮기는 FirstPersonHandMotion(60) 보다 먼저 돌면 한 프레임 뒤처져 손과 따로 논다.
// Flashlight 와 같은 이유로 순서를 뒤로 민다.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(NetworkTransform),
    typeof(ItemRigidbodySetter))]
public class ItemBase : InteractableBase, ICctvHighlightTarget {
    [SerializeField] private ItemData _itemData;

    // 사용(IUsable)/투입(IInteractionApplier) 등 이 아이템을 핫바에서 쓸 때 눌러야 하는 시간(초). 0이면 누르는 즉시 처리된다.
    [Tooltip("핫바에서 이 아이템을 사용/투입할 때 눌러야 하는 시간(초). 0이면 즉시.\n" +
             "바닥에 놓인 이 아이템을 줍는 시간과는 별개다 (그건 InteractHoldThreshold, 코드에서만 오버라이드).")]
    [SerializeField, Min(0f)] private float _itemHoldThreshold;

    // 프리팹 하나를 여러 ItemData가 공유하는 경우(예: Clue)가 있어서, 런타임에 주입된 종류를
    // 모든 클라이언트가 알 수 있도록 별도로 동기화한다.
    // 인벤토리 안에 들어가 있는 동안 true. 월드에 놓여 있으면 false.
    private readonly NetworkVariable<bool> _isStored =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 비활성화 시에 렌더링 및 Collider 모두 끄기 위해 저장할 것
    private Renderer[] _renderers;
    private Collider[] _itemColliders;
	private Outlinable _cctvOutline;
    private ItemRigidbodySetter _rigidBodySetter;
    private NetworkTransform _networkTransform;

    // 프리팹에 저장해 둔 자세(예: 눕혀 놓은 건전지). 드롭할 때 이 자세를 살려 놓기 위해 기억한다.
    private Quaternion _initialRotation = Quaternion.identity;
    private Vector3 _initialScale = Vector3.one;

    // 손에 들려 있는 동안의 앵커. null이면 손에 없는 상태.
    // NGO가 NetworkObject를 non-NetworkObject 밑으로 파렌팅하는 걸 막아서(OnTransformParentChanged
    // 검증), 실제 파렌팅 대신 매 프레임 위치·회전을 복사한다 (Flashlight와 동일한 이유).
    private Transform _handAnchor;

    // 1인칭 앵커는 프리팹에서 눈으로 맞춘 정확한 자리라 ItemData 오프셋을 더하지 않는다.
    // 캐릭터 손 본은 대략적인 기준이라 아이템마다 오프셋이 필요하다.
    private bool _handAnchorIsExact;
    private bool _isHandVisible = true;

    // 1인칭에서 제압기를 들어올릴 때처럼 한때만 크게 보여야 하는 경우에 곱한다.
    // ItemData 의 HoldScale 은 모든 화면이 공유하는 값이라 여기서 따로 얹는다.
    private float _heldScaleMultiplier = 1f;

    // 1인칭에서는 ItemData 의 1인칭 전용 크기를 쓴다. 3인칭·남의 화면은 공용 값 그대로다.
    private bool _isFirstPersonHold;

    // 1인칭에서는 뷰모델 손에 들리므로 오버레이 카메라가 그리는 레이어로 옮긴다.
    // 레이어는 클라이언트마다 따로라 남의 화면에는 영향이 없다.
    private int _defaultLayer;

    // 드롭된 뒤 바닥에 처음 닿기를 기다리는 중인지. 물리는 서버만 돌리므로 서버에서만 의미가 있다.
    private bool _awaitingDropLanding;

    // 버리는 소리를 낸 소스. 바닥에 먼저 닿으면 여기서 끊는다. 클라이언트마다 자기 것을 들고 있다.
    private AudioSource _dropSource;

    public ItemData ItemData => _itemData;
    public Quaternion InitialRotation => _initialRotation;
    public ItemType ItemId => _itemData != null ? _itemData.ItemId : ItemType.None;
    public bool IsStored => _isStored.Value;
    public float ItemHoldThreshold => _itemHoldThreshold;

    // 아이템 이름을 이어 붙이지 않고 인자로 넘긴다. 어순이 다른 언어에서 순서를 바꿀 수 있어야 한다.
    public override string InteractionText => _itemData != null
        ? LocalizeInteractionText("interact_pick_up", _itemData.DisplayName)
        : LocalizeInteractionText("interact_pick_up_generic");

    protected override void Awake()
    {
        base.Awake();

        // 아직 플레이어 밑으로 들어가거나 바닥에 안착하며 회전이 바뀌기 전이라, 지금 값이 프리팹에 저장된 자세다.
        _initialRotation = transform.localRotation;
        _initialScale = transform.localScale;

        _renderers = GetComponentsInChildren<Renderer>(true);
        _itemColliders = GetComponentsInChildren<Collider>(true);
        _rigidBodySetter = GetComponent<ItemRigidbodySetter>();
        _networkTransform = GetComponent<NetworkTransform>();

		_defaultLayer = Layers.Item;
		SetLayerRecursively(transform, Layers.Item);
		_cctvOutline = CctvHighlight.CreateOutline(this, transform, Layers.Item, _renderers, CctvHighlightKind.Item);
    }

	public override void OnDestroy()
	{
		CctvHighlight.Unregister(this);
		base.OnDestroy();
	}

	// ICctvHighlightTarget — CCTV 화면의 외곽선·이름 표시가 참조한다.
	public CctvHighlightKind CctvKind => CctvHighlightKind.Item;
	public Bounds CctvBounds => WorldBounds;
	public string CctvDisplayName => _itemData != null ? _itemData.DisplayName : null;
	public bool IsVisibleOnCctv => !IsStored;

	// 활성화된 렌더러들을 합친 월드 바운즈. CCTV 조준 표시가 화면 사각형을 잡을 때 쓴다.
	public Bounds WorldBounds => CctvHighlight.GetWorldBounds(_renderers, transform.position);

	private static void SetLayerRecursively(Transform target, int layer)
	{
		if (target == null || layer < 0)
		{
			return;
		}

		target.gameObject.layer = layer;

		foreach (Transform child in target)
		{
			SetLayerRecursively(child, layer);
		}
	}

    public override bool CanInteract(GameObject interactor)
    {
        if (_itemData == null || IsStored)
        {
            return false;
        }
        
        // 상호작용 가능 시간이면서, 상호작용 역할이 제한되어있지 않은 아이템이거나, 상호작용 가능한 대상의 역할과 일치해야 함
        return Time.time >= _interactionBlockedUntil;
    }

    private float _interactionBlockedUntil;

    //--- 런타임에 생성된 픽업 아이템의 고유 데이터 설정 ---//
    public void Configure(ItemData itemData) {
        _itemData = itemData;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isStored.OnValueChanged += HandleStoredChanged;

        // 스폰 전에 Configure로 저장한 데이터를 네트워크 등록 완료 후 동기화한다.
        
        // 늦게 들어온 클라이언트도 현재 저장된 상태를 그대로 반영해야 한다.
        ApplyStoredPresentation(_isStored.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isStored.OnValueChanged -= HandleStoredChanged;
        _handAnchor = null;
        base.OnNetworkDespawn();
    }

    private void LateUpdate()
    {
        if (_handAnchor == null)
        {
            return;
        }

        if (_handAnchorIsExact)
        {
            transform.SetPositionAndRotation(_handAnchor.position, _handAnchor.rotation);
            return;
        }

        Vector3 positionOffset = _itemData != null
            ? _itemData.ResolveHoldPositionOffset(_isFirstPersonHold)
            : Vector3.zero;
        Quaternion rotationOffset = _itemData != null
            ? Quaternion.Euler(_itemData.ResolveHoldRotationOffset(_isFirstPersonHold))
            : Quaternion.identity;
        transform.SetPositionAndRotation(_handAnchor.TransformPoint(positionOffset), _handAnchor.rotation * rotationOffset);
    }

    // PlayerItemIK가 이 아이템을 오른손에 들리거나(rightHand != null) 내려놓을 때(null) 호출한다.
    public void SetEquipped(Transform rightHand)
    {
        SetEquipped(rightHand, isExact: false);
    }

    public void SetEquipped(Transform rightHand, bool isExact)
    {
        _handAnchor = rightHand;
        _handAnchorIsExact = isExact;

        // 들고 있는 동안은 서버 권한 NetworkTransform이 위치를 되돌리지 않도록 끈다.
        if (_networkTransform != null)
        {
            _networkTransform.enabled = rightHand == null;
        }

        ApplyHeldScale();
        ApplyStoredPresentation(_isStored.Value);
    }

    // 들고 있는 동안만 크기를 더 키우거나 줄인다. 1 이면 ItemData 값 그대로다.
    public void SetHeldScaleMultiplier(float multiplier)
    {
        if (Mathf.Approximately(_heldScaleMultiplier, multiplier))
        {
            return;
        }

        _heldScaleMultiplier = multiplier;
        ApplyHeldScale();
    }

    private void ApplyHeldScale()
    {
        transform.localScale = _handAnchor != null && _itemData != null
            ? _initialScale * (_itemData.ResolveHoldScale(_isFirstPersonHold) * _heldScaleMultiplier)
            : _initialScale;
    }

    // 내 화면에서만 레이어를 옮긴다. 위치도 각 클라이언트가 따로 잡으므로 서로 간섭하지 않는다.
    public void SetFirstPersonRendering(bool useFirstPerson)
    {
        int layer = useFirstPerson ? Layers.FirstPersonHands : _defaultLayer;
        foreach (Renderer itemRenderer in _renderers)
        {
            itemRenderer.gameObject.layer = layer;
        }

        _isFirstPersonHold = useFirstPerson;
        ApplyHeldScale();
    }

    // 카트를 끌거나 총을 조준하는 등 손이 다른 데 쓰일 때 시각적으로만 숨긴다.
    public void SetHandVisible(bool isVisible)
    {
        _isHandVisible = isVisible;
        ApplyStoredPresentation(_isStored.Value);
    }

    private void HandleStoredChanged(bool previousValue, bool currentValue)
    {
        ApplyStoredPresentation(currentValue);
    }

    public void NotifyAddedToLocalInventory()
    {
        OnAdded();
    }
    // 이 아이템이 인벤토리에 새로 추가됐을 때(주웠을 때) 호출된다. 하위 클래스가 오버라이드해서
    // 자기만의 UI 반응(단서/가이드북 열기 등)을 정의한다.
    protected virtual void OnAdded() { }

    // 이 아이템이 핫바에서 선택됐을 때 호출된다. 하위 클래스가 오버라이드해서 UI 반응을 정의한다.
    public virtual void OnSelected() { }

    // 사용 완료 결과를 받은 소유자 클라이언트에서 호출된다. 단서처럼 서버 상태를 바꾸지 않고
    // 로컬 UI만 여는 아이템이 사용 완료 후 반응할 때 오버라이드한다.
    public void NotifyUseCompleted()
    {
        OnUseCompleted();
    }

    protected virtual void OnUseCompleted() { }

    // Renderer/Collider를 개별로 끄고 켠다. NGO가 비활성 NetworkBehaviour를 지원하지 않아서
    // GameObject 자체를 SetActive로 끄지 않는다.
    private void ApplyStoredPresentation(bool isStored)
    {
        // 인벤토리에 있어도 손에 들린 동안엔 렌더러만 다시 켠다 (콜라이더는 계속 꺼진 채로 둬서 못 줍게 한다).
        bool showInHand = isStored && _handAnchor != null && _isHandVisible;

        foreach (Renderer itemRenderer in _renderers)
        {
            if (itemRenderer != null)
            {
                itemRenderer.enabled = !isStored || showInHand;
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

        // 인벤토리에 들어가 있는 동안은 CCTV에도 외곽선이 보이면 안 된다.
        if (_cctvOutline != null)
        {
            _cctvOutline.enabled = !isStored;
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
    public void TryStoreItemRpc()
    {
        if (!IsSpawned || IsStored)
        {
            Debug.LogError($"[ItemBase] 스폰되지 않았거나 이미 소지 중인 아이템입니다.");
            return;
        }

        _rigidBodySetter?.Freeze();
        _isStored.Value = true;
        _awaitingDropLanding = false;
    }
    // 서버 전용: 인벤토리에서 꺼내 월드에 다시 놓는다.
    [Rpc(SendTo.Server)]
    public void DropItemToWorldRpc(Vector3 position, Quaternion rotation, Vector3 initialVelocity, float blockDuration)
    {
        if (!IsStored) {
            Debug.LogError($"[ItemBase] 소지중이지 않은 아이템을 버리려 했습니다");
            return;
        }

        transform.SetPositionAndRotation(position, rotation);

        // 부모에서 떨어지며 로컬→월드 좌표로 전환되는 순간 발생하는 이동을 순간이동으로 처리해
        // 클라이언트 화면에서 스르륵 미끄러지는 것처럼 보이지 않게 한다.
        if (_networkTransform != null) {
            // 손에 들려있던 동안 꺼놨을 수 있으니, 다시 서버가 위치를 동기화하도록 켠다.
            _networkTransform.enabled = true;
            _networkTransform.Teleport(position, rotation, transform.localScale);
        }
        _isStored.Value = false;

        Debug.Log($"[ItemBase] 아이템 드롭됨. 드롭되는 순간의 위치는 {position}, 속도는 {initialVelocity}");

        _rigidBodySetter?.Rearm(position, rotation, initialVelocity);
        BlockInteraction(blockDuration);

        // 손을 떠나는 순간과 바닥에 닿는 순간은 서로 다른 소리다.
        PlayDropSoundRpc(position);
        _awaitingDropLanding = true;
    }

    // 떨어진 아이템이 바닥에 처음 닿는 순간에 소리를 낸다. 던지는 순간에 내면 손을 떠나기도 전에
    // 울리고, 실제로 떨어진 자리와 다른 곳에서 난다.
    //
    // 물리는 서버만 돌린다(ItemRigidbodySetter). 클라이언트는 Kinematic 이라 충돌이 오지 않으므로
    // 판정도 서버가 하고 결과만 퍼뜨린다.
    private void OnCollisionEnter(Collision collision)
    {
        if (!_awaitingDropLanding || !IsServer)
        {
            return;
        }

        foreach (ContactPoint contact in collision.contacts)
        {
            // 위를 향하는 면에 닿았을 때만 바닥으로 본다. 벽에 스치는 것까지 세면
            // 던지자마자 옆 벽에서 소리가 난다.
            if (contact.normal.y <= 0.5f)
            {
                continue;
            }

            _awaitingDropLanding = false;
            PlayLandingSoundRpc(contact.point);
            return;
        }
    }

    // 소리는 각자 자기 화면에서 나야 해서 따로 보낸다. 두 소리 모두 주울 때(Item_Pickup)와 달리
    // 월드에서 나는 소리라, 그 자리에서 내고 근처 사람도 듣는다.
    //
    // 버리는 순간, 플레이어 앞에서 난다.
    [Rpc(SendTo.Everyone)]
    private void PlayDropSoundRpc(Vector3 position)
    {
        _dropSource = SoundManager.Instance?.PlayAt(SoundKey.Item_Drop, position);
    }

    // 바닥에 닿는 순간, 실제로 떨어진 자리에서 난다.
    [Rpc(SendTo.Everyone)]
    private void PlayLandingSoundRpc(Vector3 position)
    {
        // 발밑에 떨어뜨리면 버리는 소리가 끝나기도 전에 바닥에 닿는다. 그때는 앞 소리를 끊고
        // 닿는 소리만 남긴다. 둘이 겹쳐 나면 무슨 소리를 들은 건지 흐려진다.
        StopDropSound();

        SoundManager.Instance?.PlayAt(SoundKey.Item_Droped, position);
    }

    // 소스는 SoundManager 가 돌려쓰는 풀에서 온 것이라, 그 사이 다른 소리가 차지했을 수 있다.
    // 내가 튼 클립이 아직 재생 중일 때만 끊는다. (FootstepLoop 이 같은 이유로 같은 확인을 한다)
    private void StopDropSound()
    {
        if (_dropSource == null)
        {
            return;
        }

        SoundManager manager = SoundManager.Instance;
        if (_dropSource.isPlaying && manager != null && manager.Owns(SoundKey.Item_Drop, _dropSource.clip))
        {
            _dropSource.Stop();
        }

        _dropSource = null;
    }

    // 서버 전용: 소비/소모되어 완전히 사라지는 경우.
    [Rpc(SendTo.Server)]
    public void DestroyRpc() {
        
        if (!IsSpawned) {
            Debug.LogError($"[ItemBase] 스폰되지 않은 아이템을 삭제하려 했습니다.");
            return;
        }

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

        // 단서는 인벤토리 슬롯을 쓰지 않는다. 번호를 전원 단서 목록에 공유하고 월드 오브젝트는 없앤다.
        // (슬롯을 안 거치므로 인벤토리가 꽉 차 있어도 획득이 막히지 않는다.)
        if (this is ClueItem clue)
        {
            ShareClueOnServer(clue);
            return;
        }

        PlayerInventory inventory = player.GetComponent<PlayerInventory>();

        // 서버에서 인벤토리 공간을 확인하고, 되면 저장까지 한 번에 처리한다 (PickUpItemRpc 내부에서 TryStoreItemRpc 호출).
        inventory.PickUpItemRpc(new NetworkBehaviourReference(this));
    }

    // 서버 전용: 한 명이 찾은 단서는 현재 접속한 모든 플레이어의 목록에 넣는다.
    // 각 클라이언트의 로컬 PlayerClueBook 변경 이벤트가 발생하므로 알림과 목록도 전원에게 동일하게 표시된다.
    private static void ShareClueOnServer(ClueItem clue)
    {
        PlayerClueBook[] clueBooks = FindObjectsByType<PlayerClueBook>(FindObjectsSortMode.None);
        if (clueBooks.Length == 0)
        {
            Debug.LogError("[ItemBase] PlayerClueBook을 찾지 못해 단서를 공유하지 못했습니다.", clue);
            return;
        }

        foreach (PlayerClueBook clueBook in clueBooks)
        {
            clueBook.TryAddClueOnServer(clue.ClueNumber);
        }

        clue.NetworkObject.Despawn(destroy: true);
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
