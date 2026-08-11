using Unity.Netcode;
using UnityEngine;

// 플레이어 상호작용의 코어: 참조/라이프사이클과 프레임별 입력 분배를 담당한다.
// 세부 로직은 partial로 분리되어 있다.
//   - PlayerInteraction.Targeting.cs            : 화면 중심 조준으로 대상 감지·선택
//   - PlayerInteraction.ItemUI.cs               : 아이템 획득 시 단서·가이드 북 UI 여닫기
[RequireComponent(typeof(PlayerInventory), typeof(PlayerHealth))]
public partial class PlayerInteraction : NetworkBehaviour
{
    // 인스펙터에서 연결하는 참조와 상호작용 범위를 조절하는 값.
    [Header("상호작용 설정")]
    [SerializeField] private Camera _playerCamera;
    [Range(0.01f, 0.5f)]
    [SerializeField] private float _screenCenterRadius = 0.2f; // 화면 중심에서 상호작용 가능한 영역의 반지름
    [SerializeField] private InteractionPromptUI _promptUI;
    [SerializeField, Min(0.1f)] private float _reviveHoldDuration = 1.2f;

    private InteractableBase _currentTarget;  // 현재 상호작용 가능한 대상

    private CustomInputActions _actions;
    private PlayerInventory _inventory;
    private PlayerHealth _health;
    private PlayerItemUse _itemUse;
    private HoldAction _activeHoldAction = HoldAction.None;
    private float _holdTimer;
    private float _holdDuration;
    private float _promptRefreshUntil; // 서버 상호작용 결과가 늦게 도착해도 안내 문구가 바로 바뀌도록 잠깐만 재확인한다.
    private InteractableBase _holdTarget;

    private enum HoldAction
    {
        None,
        UseItem,
        Revive,
        Interactable,
        ApplyItem
    }

    public CartBase CarryingCart { get; set; } // 플레이어가 끌고 있는 카트. null이면 카트를 끌고 있지 않다.

    private void Awake()
    {
        _actions = new CustomInputActions();
        _inventory = GetComponent<PlayerInventory>();
        _health = GetComponent<PlayerHealth>();
        _itemUse = GetComponent<PlayerItemUse>();

        if (_inventory == null)
        {
            Debug.LogError("[PlayerInteraction] PlayerInventory가 없어 상호작용 인벤토리 처리를 할 수 없습니다.", this);
        }

        if (_health == null)
        {
            Debug.LogError("[PlayerInteraction] PlayerHealth가 없어 다운 상태를 확인할 수 없습니다.", this);
        }
    }

    private void OnEnable()
    {
        _actions ??= new CustomInputActions();
        _actions.Enable();
    }

    private void OnDisable()
    {
        SetCurrentTarget(null);
        _actions?.Disable();
    }

    public override void OnNetworkSpawn()
    {
        // 입력과 UI는 이 플레이어를 조작하는 클라이언트에서만 초기화한다.
        if (!IsOwner) {
            _actions.Disable();
            return;
        }

        InitializeOnGameScene();

        if (_inventory != null)
        {
            _inventory.OnInventoryChanged += HandleInventoryChanged;
        }
    }

    // 대기방에서 스폰된 채로 게임씬까지 파괴되지 않고 유지되는 플레이어 오브젝트는
    // OnNetworkSpawn이 대기방에서 한 번만 실행되어 게임씬 전용 UI를 못 찾는다.
    // 게임씬 로드가 끝난 뒤 GameSessionManager가 이 함수만 다시 호출해 UI 참조를 갱신한다
    // (이벤트 재구독까지 같이 도는 OnNetworkSpawn() 전체 재호출은 이중 구독을 유발하므로 피한다).
    public void InitializeOnGameScene()
    {
        if (_promptUI == null) {
            _promptUI = FindFirstObjectByType<InteractionPromptUI>(FindObjectsInactive.Include);
        }

        _itemUse?.BindInteractionPromptUI(_promptUI);
    }

    public override void OnNetworkDespawn()
    {
        if (_inventory != null)
        {
            _inventory.OnInventoryChanged -= HandleInventoryChanged;
        }
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        // 쓰러지면 상호작용 불가능하게 + 혹시라도 카트와 상호작용중이었다면 카트 놓도록
        if (_health.IsDowned)
        {
            CancelHoldAction();
            SetCurrentTarget(null);
            return;
        }

        if (CarryingCart != null)
        {
            CancelHoldAction();
            UpdateCartInteraction();
            return;
        }

        if (GameplayUiMode.IsActive)
        {
            CancelHoldAction();
            SetCurrentTarget(null);

            if (_actions.Player.Interact.WasPressedThisFrame())
            {
                // 가이드 북이 열려 있으면 먼저 닫고, 아니면 단서 UI를 닫는다.
                if (!TryCloseGuideBook())
                {
                    TryCloseVisibleClue();
                }
            }

            return;
        }

        UpdateCurrentTarget();

        // 상호작용 안내 문구를 갱신한다. (조준 대상이 없으면 안내 문구를 비운다)   
        if (_currentTarget != null && Time.time <= _promptRefreshUntil)
        {
            RefreshInteractionPrompt();
        }
        UpdateHoldAction();

        // 조준 대상을 갱신한 뒤 상호작용과 드롭 입력을 처리한다.
        if (_actions.Player.Interact.WasPressedThisFrame()) // 상호작용 버튼이 눌렸을 때
        {
            HandleInteractInput();
        }
        if (_actions.Player.Drop.WasPressedThisFrame()) // 드롭 버튼이 눌렸을 때
        {
            CancelHoldAction();
            DropSelectedItem();
        }
    }

    private void HandleInteractInput()
    {
        // 대상을 조준 중이고 그 대상에 적용 가능한 IInteractionApplier 아이템을 들고 있으면 최우선으로 적용을 시도한다.
        if (_currentTarget != null && TryGetApplierForTarget(_currentTarget, out ItemBase applierItem, out _))
        {
            BeginHoldAction(HoldAction.ApplyItem, applierItem.HoldDuration, _currentTarget);
            return;
        }

        // 조준 중인 대상이 있으면 단서 UI보다 필드 상호작용을 우선한다.
        if (_currentTarget != null)
        {
            if (_currentTarget is PlayerReviveInteractable)
            {
                BeginHoldAction(HoldAction.Revive, _reviveHoldDuration, _currentTarget);
            }

            //  상호작용 대상이 길게 누르기 상호작용을 요구하면 HoldAction을 시작하고, 아니면 즉시 상호작용을 시도한다.
            else if (_currentTarget.RequiresHoldInteraction(gameObject))
            {
                BeginHoldAction(HoldAction.Interactable, _currentTarget.HoldInteractionDuration, _currentTarget);
            }

            else
            {
                TryInteract();
            }

            return;
        }

        if (TryCloseVisibleClue())
        {
            return;
        }

        // 선택한 아이템이 사용 가능하면 그 처리를 우선하고, 아니면 단서 UI를 연다.
        if (_inventory != null && _inventory.TryGetSelectedItemBase(out ItemBase item) && item is IUsable usable)
        {
            if (usable.CanUse(gameObject, out string message))
            {
                if (usable.RequiresHold)
                {
                    BeginHoldAction(HoldAction.UseItem, item.HoldDuration);
                    return;
                }

                if (_itemUse != null && _itemUse.TryCompleteSelectedItemUse(out string doneMessage))
                {
                    _promptUI?.ShowTemporaryPrompt(doneMessage);
                }
                return;
            }

            if (message != null)
            {
                _promptUI?.ShowTemporaryPrompt(message);
                return;
            }
        }

        TryShowSelectedItemUi();
    }

    // 현재 선택한 아이템이 target에 적용 가능한 IInteractionApplier인지 확인한다.
    private bool TryGetApplierForTarget(InteractableBase target, out ItemBase item, out IInteractionApplier applier)
    {
        item = null;
        applier = null;

        if (target == null
            || _inventory == null
            || !_inventory.TryGetSelectedItemBase(out ItemBase selected)
            || selected is not IInteractionApplier itemApplier
            || !itemApplier.CanApplyTo(gameObject, target, out _))
        {
            return false;
        }

        item = selected;
        applier = itemApplier;
        return true;
    }

    private void TryInteract()
    {
        if (_currentTarget == null)
        {
            return;
        }

        // 선택을 먼저 해제해 아웃라인과 안내 문구를 즉시 갱신한다.
        IInteractable target = _currentTarget;
        SetCurrentTarget(null);
        target.Interact(gameObject);
    }

    // 선택한 아이템을 월드에 드롭한다. 실제 반영(검증/제거/재배치)은 PlayerInventory.RequestDropRpc가 담당한다.
    private void DropSelectedItem()
    {
        if (_inventory == null || _playerCamera == null) { return; }

        // 선택 슬롯이 비어있는지는 서버(TryTakeSelectedItemOnServer)가 재검증하므로 여기서 따로 안 막는다.
        _inventory.TryGetSelectedItemId(out ItemType itemId);

        Transform cameraTransform = _playerCamera.transform;
        Vector3 dropPosition = cameraTransform.position + cameraTransform.forward * 1f;
        Vector3 dropVelocity = cameraTransform.forward * 2f + Vector3.up;

        _inventory.RequestDropRpc(itemId, _inventory.SelectedIndex, dropPosition, dropVelocity);
        TryCloseVisibleClue();
    }

    private void BeginHoldAction(HoldAction action, float duration, InteractableBase target = null)
    {
        _activeHoldAction = action;
        _holdTimer = 0f;
        _holdDuration = duration;
        _holdTarget = target;
        _promptUI?.SetUseHoldProgress(0f, true);
    }

    private void UpdateHoldAction()
    {
        if (_activeHoldAction == HoldAction.None)
        {
            return;
        }

        if (!_actions.Player.Interact.IsPressed() || !IsHoldActionStillValid())
        {
            CancelHoldAction();
            return;
        }

        _holdTimer += Time.deltaTime;
        float progress = Mathf.Clamp01(_holdTimer / _holdDuration);
        _promptUI?.SetUseHoldProgress(progress, true);

        if (progress < 1f)
        {
            return;
        }

        CompleteHoldAction();
    }

    private bool IsHoldActionStillValid()
    {
        // holdAction은 아이템을 사용하거나, 소생시키거나, holdTarget과의 상호작용시에만 적용됩니다.
        return _activeHoldAction switch
        {
            HoldAction.UseItem => _currentTarget == null,
            HoldAction.Revive => _holdTarget != null && ReferenceEquals(_currentTarget, _holdTarget) && _holdTarget.CanInteract(gameObject),
            HoldAction.Interactable => _holdTarget != null && ReferenceEquals(_currentTarget, _holdTarget) && _holdTarget.CanInteract(gameObject),
            HoldAction.ApplyItem => _holdTarget != null && ReferenceEquals(_currentTarget, _holdTarget) && TryGetApplierForTarget(_holdTarget, out _, out _),
            _ => false
        };
    }

    private void CompleteHoldAction()
    {
        HoldAction completedAction = _activeHoldAction;
        InteractableBase completedTarget = _holdTarget;
        CancelHoldAction();

        switch (completedAction)
        {
            case HoldAction.UseItem:
                if (_itemUse != null && _itemUse.TryCompleteSelectedItemUse(out string message))
                {
                    _promptUI?.ShowTemporaryPrompt(message);
                }
                break;

            case HoldAction.Revive:
                if (completedTarget != null && completedTarget.CanInteract(gameObject))
                {
                    completedTarget.Interact(gameObject);
                }
                break;

            case HoldAction.Interactable:
                if (completedTarget != null && completedTarget.CanInteract(gameObject))
                {
                    completedTarget.Interact(gameObject);
                    _promptRefreshUntil = Time.time + 0.75f;
                    RefreshInteractionPrompt();
                }
                break;

            case HoldAction.ApplyItem:
                if (completedTarget != null && TryGetApplierForTarget(completedTarget, out ItemBase completedItem, out _))
                {
                    RequestApplyItemRpc(new NetworkBehaviourReference(completedItem), new NetworkBehaviourReference(completedTarget), _inventory.SelectedIndex);
                    _promptRefreshUntil = Time.time + 0.75f;
                    RefreshInteractionPrompt();
                }
                break;
        }
    }

    // 조준 대상에 들고 있는 IInteractionApplier 아이템을 적용한다 (예: 오염 샘플을 미션 기계에 투입).
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestApplyItemRpc(NetworkBehaviourReference itemRef, NetworkBehaviourReference targetRef, int selectedIndex)
    {
        if (!itemRef.TryGet(out ItemBase item) || item is not IInteractionApplier applier) { return; }
        if (!targetRef.TryGet(out InteractableBase target) || !target.CanInteract(gameObject)) { return; }
        if (!applier.CanApplyTo(gameObject, target, out _)) { return; }

        applier.ApplyToOnServer(gameObject, target, _inventory, selectedIndex);
    }

    private void CancelHoldAction()
    {
        if (_activeHoldAction == HoldAction.None)
        {
            return;
        }

        _activeHoldAction = HoldAction.None;
        _holdTimer = 0f;
        _holdDuration = 0f;
        _holdTarget = null;
        _promptUI?.SetUseHoldProgress(0f, false);
    }
}
