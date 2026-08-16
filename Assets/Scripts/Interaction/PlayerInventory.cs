using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    private const int InventorySize = 4;

    // SelectedIndex가 이 값이면 아이템 선택이 잠긴 상태(카트 끄는 중 등)라는 뜻.
    // 그 어떤 슬롯도 가리키지 않으므로 TryGetSelectedItem...류가 전부 자동으로 "선택 없음"을 반환한다.
    public const int NoSelectionIndex = -1;

    [SerializeField, Min(0f)] private float _dropInteractionDelay = 1.5f;   // 드롭 후 상호작용 차단 시간

    // 슬롯 내용은 서버만 쓰고 전원이 읽는다
    private readonly NetworkList<InventorySlot> _slots = new(
        readPerm: NetworkVariableReadPermission.Everyone,
        writePerm: NetworkVariableWritePermission.Server);

    // 미션에 맡긴 아이템 인스턴스. 슬롯을 차지하지 않는 동안에도 오브젝트 자체는 유지된다
    // (완료 시 DiscardMissionItems가 실제로 파괴한다). 동기화 안 되는 로컬 리스트라 각 피어가
    // 각자 채운다 - 슬롯(_slots)이 전원 읽기라서 어느 피어든 참조를 얻을 수 있다.
    private readonly List<ItemBase> _missionItems = new();

    private readonly NetworkVariable<int> _selectedIndex =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // 라운드 시작에 지급할 손전등 데이터. Inspector 참조를 비우는 것만으로 기본 지급을 끌 수 있다.
    [Tooltip("비워 두면 시작 손전등을 지급하지 않습니다.")]
    [SerializeField] private ItemData _startingFlashlight;

    private int _preCartSelectedIndex;

    // 현재 OnEquipped가 적용된 실제 아이템 인스턴스.
    // 선택 갱신 때 같은 아이템을 중복 장착하지 않고, 교체 전에 정확한 이전 아이템을 해제하는 데 사용한다.
    private ItemBase _equippedItem;

    public NetworkList<InventorySlot> Slots => _slots;
    public IReadOnlyList<ItemBase> MissionItems => _missionItems;
    public int SelectedIndex => _selectedIndex.Value;
    public bool HasAnyItem => FindFirstOccupiedSlot() >= 0;
    public bool IsFull => FindEmptySlot() < 0;

    public event Action OnInventoryChanged;
    public event Action<int> OnSlotSelected;

    private CustomInputActions _actions;
    private Camera _playerCamera;
    private PlayerHealth _health;
    private PlayerInteraction _interaction;
    private InventoryUI _inventoryUI;

    private void Awake()
    {
        _playerCamera = GetComponentInChildren<Camera>(true);
        _health = GetComponent<PlayerHealth>();
        _interaction = GetComponent<PlayerInteraction>();
    }
    private void OnEnable()
    {
        _actions ??= new CustomInputActions();
        _actions.Enable();
    }

    private void OnDisable()
    {
        _actions?.Disable();
    }

    // 1, 2, 3, 4번 누르면 각 슬롯 선택하도록
    private void Update()
    {
        // SelectedIndex가 잠긴 상태(카트 끄는 중 등)면 입력을 받지 않는다.
        if (!IsOwner || _selectedIndex.Value == NoSelectionIndex)
        {
            return;
        }

        if (_actions.Player.Slot1.WasPressedThisFrame())
            SelectSlot(0);

        if (_actions.Player.Slot2.WasPressedThisFrame())
            SelectSlot(1);

        if (_actions.Player.Slot3.WasPressedThisFrame())
            SelectSlot(2);

        if (_actions.Player.Slot4.WasPressedThisFrame())
            SelectSlot(3);

        float scrollY = _actions.Player.InventoryScroll.ReadValue<Vector2>().y;
        if (scrollY != 0f)
            SelectSlotByScroll(scrollY);
        if (_actions.Player.Drop.WasPressedThisFrame())
        {
            TryDropSelectedItem();
        }
    }

    // 휠 굴리면 휠로 아이템 선택
    private void SelectSlotByScroll(float scrollY)
    {
        int direction = scrollY > 0f ? -1 : 1;
        int nextIndex = (_selectedIndex.Value + direction + InventorySize) % InventorySize;

        SelectSlot(nextIndex);
    }

    public override void OnNetworkSpawn()
    {
        _slots.OnListChanged += HandleSlotsChanged;

        // 선택 번호는 Owner가 바꾸고 전원에게 동기화되므로 각 피어가 같은 장착 표시를 갱신한다.
        _selectedIndex.OnValueChanged += HandleSelectedIndexChanged;
        
        // 슬롯 초기 구성은 서버만 한다. NetworkList는 스폰 이후에만 쓸 수 있어 여기서 채운다.
        if (IsServer)
        {
            for (int i = 0; i < InventorySize; i++)
            {
                _slots.Add(InventorySlot.Empty);
            }
        }

        // 초기 NetworkList를 받은 시점에도 현재 선택 슬롯을 한 번 평가해 장착 상태를 맞춘다.
        RefreshEquippedItem();
    }
    
    // 이 인벤토리를 조작하는 클라이언트에서만 씬의 인벤토리 UI를 나 자신에게 연결한다.
    public void InitializeOnGameScene() {
        if (!IsOwner) return;

        _inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        _inventoryUI?.Refresh(this);
    }

    // CartBase가 카트를 잡거나 놓을 때 호출한다. 카트 사용 중엔 선택을 -1로 잠그고, 놓으면 이전 선택으로 복원한다.
    public void SetCartCarrying(bool isCarrying)
    {
        if (!IsOwner) return;

        if (isCarrying)
        {
            _preCartSelectedIndex = _selectedIndex.Value;
            _selectedIndex.Value = NoSelectionIndex;
        }
        else
        {
            _selectedIndex.Value = _preCartSelectedIndex;
        }

        NotifyInventoryChanged();
    }

    public override void OnNetworkDespawn()
    {
        // 플레이어가 사라진 뒤 손전등 모델이나 광원이 남지 않도록 현재 장착 효과부터 해제한다.
        UnequipCurrentItem();

        _slots.OnListChanged -= HandleSlotsChanged;

        // 디스폰된 인벤토리로 선택 변경 콜백이 들어오지 않게 스폰 때 등록한 구독을 해제한다.
        _selectedIndex.OnValueChanged -= HandleSelectedIndexChanged;
    }
    
    // 슬롯 변경을 감지하고 필요한 이벤트를 호출한다.
    // "방금 주웠다" 반응(단서/가이드북 UI 열기 등)은 ItemBase.OnAdded가 아이템 자신의 상태
    // 변화(IsStored)로 직접 감지하므로 여기서는 신경 쓰지 않는다.
    private void HandleSlotsChanged(NetworkListEvent<InventorySlot> changeEvent)
    {
        NotifyInventoryChanged();

        // 선택 번호가 같아도 해당 슬롯의 아이템이 추가·제거될 수 있으므로 장착 대상을 다시 확인한다.
        RefreshEquippedItem();

        if (!IsOwner || changeEvent.Index < 0 || changeEvent.Index >= _slots.Count)
        {
            return;
        }

        if (_slots[changeEvent.Index].TryGetItem(out ItemBase item))
        {
            _interaction?.RemoveNearbyInteractable(item);
            item.NotifyAddedToLocalInventory();
        }
    }

    // 동기화된 선택 번호가 바뀌면 새 슬롯을 기준으로 장착 대상을 다시 계산한다.
    private void HandleSelectedIndexChanged(int previousValue, int currentValue)
    {
        RefreshEquippedItem();
    }

    // 선택 슬롯의 IEquippable을 이전 장착 아이템과 비교해 필요한 해제·장착만 한 번씩 호출한다.
    private void RefreshEquippedItem()
    {
        // 선택이 없거나 선택 아이템이 장착형이 아니면 다음 장착 대상은 null이다.
        ItemBase nextEquippedItem = null;

        if (TryGetSelectedItemBase(out ItemBase selectedItem) && selectedItem is IEquippable)
        {
            // 인터페이스만 저장하지 않고 ItemBase 인스턴스를 보관해 동일 네트워크 아이템인지 비교한다.
            nextEquippedItem = selectedItem;
        }

        // 슬롯 목록과 선택 번호 알림이 연달아 와도 같은 아이템의 장착 콜백을 중복 실행하지 않는다.
        if (ReferenceEquals(_equippedItem, nextEquippedItem))
        {
            return;
        }

        // 새 대상을 덮어쓰기 전에 이전 아이템이 만든 모델과 효과를 먼저 정리한다.
        UnequipCurrentItem();

        // 현재 장착 대상을 먼저 기록해 이후 슬롯 변경에서도 같은 인스턴스를 식별할 수 있게 한다.
        _equippedItem = nextEquippedItem;

        if (_equippedItem is IEquippable equippable)
        {
            // 이 PlayerInventory의 GameObject를 전달해 아이템이 해당 플레이어의 표시 컴포넌트를 찾게 한다.
            equippable.OnEquipped(gameObject);
        }
    }

    // 현재 아이템의 장착 효과를 해제하고 추적 참조를 비운다.
    private void UnequipCurrentItem()
    {
        if (_equippedItem is IEquippable equippable)
        {
            equippable.OnUnequipped(gameObject);
        }

        // 이미 해제한 아이템에 OnUnequipped를 다시 보내지 않도록 항상 null로 마무리한다.
        _equippedItem = null;
    }

    // item은 이미 스폰된 상태여야 한다 (월드에 있던 것을 줍거나, 방금 스폰해서 바로 넣는 경우 모두).
    [Rpc(SendTo.Server)]
    public void PickUpItemRpc(NetworkBehaviourReference itemRef)
    {
        if (!itemRef.TryGet(out ItemBase item) || item == null) {
            Debug.LogError($"[PlayerInventory] 존재하지 않는 아이템을 주우려 했습니다.");
            return;
        }

        int emptySlotIndex = FindEmptySlot();

        if (emptySlotIndex < 0) {
            Debug.LogWarning("[PlayerInventory] 인벤토리가 가득 찼습니다.");

            // 자리가 없는 건 서버만 알 수 있으니, 주우려 한 본인 화면에 이유를 알려준다.
            ShowInventoryFullMessageOwnerRpc(RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
            return;
        }

        // 아이템 자체가 주울 수 있는 상태인지 체크. RPC는 void라 결과는 IsStored로 확인한다.
        item.TryStoreItemRpc();
        if (!item.IsStored) { return; }

        _slots[emptySlotIndex] = new InventorySlot { ItemRef = new NetworkBehaviourReference(item) };

        // 아이템을 주우면 새로 주운 슬롯에 커서가 가게 한다.
        // _selectedIndex는 Owner만 쓸 수 있는 NetworkVariable이라 서버가 대신 못 쓰니, 오너 클라이언트에게 Rpc로 시킨다.
        ChangeSelectedNumberRpc(emptySlotIndex, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));

        Debug.Log($"Slot{emptySlotIndex + 1}에 '{item.ItemId}' 추가");
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void ShowInventoryFullMessageOwnerRpc(RpcParams rpcParams = default)
    {
        FindFirstObjectByType<InteractionPromptUI>(FindObjectsInactive.Include)
            ?.ShowTemporaryPrompt("인벤토리가 가득 찼습니다");
    }

    // 주워서 커서가 옮겨 가는 경우다. 직접 고른 게 아니므로 OnSlotSelected(아이템 안내 표시)는 알리지 않는다.
    [Rpc(SendTo.SpecifiedInParams)]
    private void ChangeSelectedNumberRpc(int index, RpcParams rpcParams = default)
    {
        _selectedIndex.Value = index;
        NotifyInventoryChanged();
        NotifySelectedItem();
    }

    private void NotifySelectedItem()
    {
        if (TryGetSelectedItemBase(out ItemBase item))
        {
            item.OnSelected();
        }
    }

    // 선택한 아이템을 월드에 드롭한다. 새 오브젝트를 만드는 게 아니라, 인벤토리에 들어가 있던
    // (reparent된) 같은 인스턴스를 다시 꺼내 월드로 되돌린다 - 그래서 인스턴스가 들고 있던 상태
    // (예: 손전등 배터리 잔량)가 유지된다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void RequestDropRpc(ItemType itemId, int selectedIndex, Vector3 dropPosition, Vector3 dropVelocity)
    {
        // 실제로 그 슬롯에 그 종류가 있었는지는 TryTakeSelectedItemOnServer 내부에서 재검증한다.
        if (!TryTakeSelectedItemOnServer(itemId, selectedIndex, out ItemBase item)) {
            Debug.LogError($"[PlayerInventory] 선택한 슬롯의 아이템을 드롭하지 못했습니다.");
            return;
        }

        // 플레이어가 바라보는 Y축 방향으로 내려놓되, 프리팹에 저장된 자세는 그대로 살린다.
        // (건전지처럼 눕혀 놓은 아이템이 세워진 채로 떨어지지 않게 한다.)
        Quaternion dropRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * item.InitialRotation;
        item.DropItemToWorldRpc(dropPosition, dropRotation, dropVelocity, _dropInteractionDelay);
    }

    private void TryDropSelectedItem()
    {
        // UI 켜져있거나, 체력이 0이거나, 카트 끌고 있거나, 카메라 없거나, 선택된 아이템이 없다면 제외
        if (GameplayUiMode.IsActive
            || (_health != null && _health.IsDowned)
            || (_interaction.CarryingCart != null)
            || _playerCamera == null
            || !TryGetSelectedItemId(out ItemType itemId))
        {
            return;
        }

        Transform cameraTransform = _playerCamera.transform;
        Vector3 dropPosition = cameraTransform.position + cameraTransform.forward * 1f;
        Vector3 dropVelocity = cameraTransform.forward * 2f + Vector3.up;
        RequestDropRpc(itemId, SelectedIndex, dropPosition, dropVelocity);
    }
    // 휠이나 1~4번으로 직접 고른 경우다. 이때만 OnSlotSelected로 아이템 안내를 띄운다.
    private void SelectSlot(int index)
    {
        if (index < 0 || index >= InventorySize)
            return;

        _selectedIndex.Value = index;
        NotifyInventoryChanged();
        OnSlotSelected?.Invoke(index);
        NotifySelectedItem();
    }

    private void NotifyInventoryChanged()
    {
        OnInventoryChanged?.Invoke();
        _inventoryUI?.Refresh(this);
    }

    public bool TryGetSelectedItemId(out ItemType itemId)
    {
        itemId = ItemType.None;

        if (!TryGetSelectedItemBase(out ItemBase item)) {
            return false;
        }

        itemId = item.ItemId;
        return true;
    }

    public bool TryGetSelectedItemBase(out ItemBase item)
    {
        item = null;

        // _selectedIndex는 항상 0~InventorySize-1 범위지만, 방어적으로 범위를 다시 확인한다.
        // 선택된 슬롯이 비어있는 것도 정상 상태라 에러가 아니다 - 아래 TryGetItem이 false를 돌려줄 뿐이다.
        if (_selectedIndex.Value < 0 || _selectedIndex.Value >= _slots.Count) {
            return false;
        }

        return _slots[_selectedIndex.Value].TryGetItem(out item);
    }

    // 소비/소모되어 완전히 사라지는 경우 (에너지바 사용, 추적기 부착, 안테나 설치 등).
    public bool TryRemoveSelectedItemOnServer(ItemType expectedItemId, int selectedIndex)
    {
        if (!IsServer || !TryGetItemAt(selectedIndex, expectedItemId, out ItemBase item)) {
            return false;
        }

        RemoveItemAt(selectedIndex);
        item.DestroyRpc();
        return true;
    }

    // 슬롯에서만 떼어내고 오브젝트는 살려둔다 (드롭이 사용 - 같은 인스턴스를 월드로 되돌린다).
    public bool TryTakeSelectedItemOnServer(ItemType expectedItemId, int selectedIndex, out ItemBase item)
    {
        item = null;

        if (!IsServer || !TryGetItemAt(selectedIndex, expectedItemId, out item))
        {
            return false;
        }

        RemoveItemAt(selectedIndex);
        return true;
    }

    // 미션에 아이템 한 개를 넘기고 일반 인벤토리 슬롯에서는 제거한다. 오브젝트는 파괴하지 않는다.
    public bool MoveItemToMission(ItemType itemId)
    {
        if (itemId == ItemType.None || !TryFindItemByType(itemId, out ItemBase item))
        {
            return false;
        }

        if (IsServer)
        {
            return MoveItemToMissionLocally(item);
        }

        if (!IsOwner || !MoveItemToMissionLocally(item))
        {
            return false;
        }

        RemoveMissionItemServerRpc(new NetworkBehaviourReference(item));
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RemoveMissionItemServerRpc(NetworkBehaviourReference itemRef)
    {
        if (itemRef.TryGet(out ItemBase item))
        {
            MoveItemToMissionLocally(item);
        }
    }

    // 미션 종료 시 보관함의 모든 아이템을 인벤토리로 반환하지 않고 폐기(파괴)한다.
    public void DiscardMissionItems()
    {
        if (!IsServer)
        {
            if (!IsOwner)
            {
                return;
            }

            // 오너 쪽은 실제 오브젝트를 파괴할 권한이 없다 - 목록만 비우고 서버에 위임한다.
            _missionItems.Clear();
            DiscardMissionItemsServerRpc();
            return;
        }

        DisposeAllMissionItems();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void DiscardMissionItemsServerRpc()
    {
        DisposeAllMissionItems();
    }

    private void DisposeAllMissionItems()
    {
        foreach (ItemBase item in _missionItems)
        {
            item?.DestroyRpc();
        }

        _missionItems.Clear();
    }

    private bool MoveItemToMissionLocally(ItemBase item)
    {
        if (!RemoveItemByReferenceLocally(item))
        {
            return false;
        }

        _missionItems.Add(item);
        return true;
    }

    private bool RemoveItemByReferenceLocally(ItemBase item)
    {
        int itemIndex = FindItemSlotByReference(item);
        if (itemIndex < 0)
        {
            return false;
        }

        // 슬롯(NetworkList)은 서버 권한이라 서버에서만 실제로 지운다. 오너 쪽에서 이 메서드가
        // 먼저 로컬로 불려도, 슬롯이 비는 건 서버가 처리한 뒤 NetworkList 동기화로 반영된다.
        if (IsServer)
        {
            RemoveItemAt(itemIndex);
        }

        return true;
    }

    private bool TryGetItemAt(int index, ItemType expectedItemId, out ItemBase item)
    {
        item = null;

        if (index < 0 || index >= _slots.Count)
        {
            return false;
        }

        return _slots[index].TryGetItem(out item) && item.ItemId == expectedItemId;
    }

    private void RemoveItemAt(int index)
    {
        _slots[index] = InventorySlot.Empty;
        Debug.Log($"Slot{index + 1}에서 아이템 제거");
    }

    private bool TryFindItemByType(ItemType itemId, out ItemBase item)
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].TryGetItem(out item) && item.ItemId == itemId)
            {
                return true;
            }
        }

        item = null;
        return false;
    }

    private int FindItemSlotByReference(ItemBase item)
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].TryGetItem(out ItemBase slotItem) && slotItem == item)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindEmptySlot()
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].IsEmpty)
                return i;
        }
        return -1;
    }

    private int FindFirstOccupiedSlot()
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            if (!_slots[i].IsEmpty)
                return i;
        }
        return -1;
    }

    // RoundManager가 라운드 인벤토리를 비운 뒤 호출해 설정된 손전등을 실제 네트워크 아이템으로 지급한다.
    public void GrantStartingFlashlightOnServer()
    {
        // 네트워크 스폰과 서버 쓰기 NetworkList 변경은 서버만 수행한다.
        // ItemData가 비어 있으면 기본 지급을 사용하지 않는 설정이므로 그대로 종료한다.
        if (!IsServer || _startingFlashlight == null)
        {
            return;
        }

        // 기존 아이템 생성·보관 경로를 사용해 빈 슬롯 탐색, 네트워크 스폰, 선택 슬롯 이동까지 동일하게 처리한다.
        ItemBase.TrySpawnAndAddToInventory(_startingFlashlight, this);
    }

    public void ClearAllItemsOnServer()
    {
        if (!IsServer) {
            return;
        }

        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].TryGetItem(out ItemBase item))
            {
                item.DestroyRpc();
            }

            _slots[i] = InventorySlot.Empty;
        }

        DisposeAllMissionItems();

        if (IsOwner)
        {
            _selectedIndex.Value = 0;
        }
        else
        {
            // _missionItems는 동기화 안 되는 로컬 리스트라, 원격 플레이어의 화면 쪽도 따로 비워줘야 한다.
            ClearOwnerMissionStateRpc(RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void ClearOwnerMissionStateRpc(RpcParams rpcParams = default)
    {
        _missionItems.Clear();
        _selectedIndex.Value = 0;
    }
}
