using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    private const int InventorySize = 4;

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
        if (!IsOwner)
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
        
        // 슬롯 초기 구성은 서버만 한다. NetworkList는 스폰 이후에만 쓸 수 있어 여기서 채운다.
        if (IsServer)
        {
            for (int i = 0; i < InventorySize; i++)
            {
                _slots.Add(InventorySlot.Empty);
            }
        }
    }
    
    // 이 인벤토리를 조작하는 클라이언트에서만 씬의 인벤토리 UI를 나 자신에게 연결한다.
    public void InitializeOnGameScene() {
        if (IsOwner) { FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include)?.BindInventory(this); }
    }

    public override void OnNetworkDespawn()
    {
        _slots.OnListChanged -= HandleSlotsChanged;
    }
    
    // 슬롯 변경을 감지하고 필요한 이벤트를 호출한다.
    // "방금 주웠다" 반응(단서/가이드북 UI 열기 등)은 ItemBase.OnAdded가 아이템 자신의 상태
    // 변화(IsStored)로 직접 감지하므로 여기서는 신경 쓰지 않는다.
    private void HandleSlotsChanged(NetworkListEvent<InventorySlot> changeEvent)
    {
        OnInventoryChanged?.Invoke();

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
    private void ChangeSelectedNumberRpc(int index, RpcParams rpcParams = default)
    {
        _selectedIndex.Value = index;
        OnInventoryChanged?.Invoke();
        OnSlotSelected?.Invoke(index);
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

        // 플레이어가 바라보는 Y축 방향으로 내려놓는다.
        Quaternion dropRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
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
    private void SelectSlot(int index)
    {
        if (index < 0 || index >= InventorySize)
            return;

        _selectedIndex.Value = index;
        OnInventoryChanged?.Invoke();
        OnSlotSelected?.Invoke(index);
        NotifySelectedItem();
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
        if (!IsServer || !TryGetItemAt(selectedIndex, expectedItemId, out ItemBase item))
        {
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
