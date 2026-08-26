using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;

// 플레이어 상호작용: 참조/라이프사이클, 프레임별 입력 분배, 화면 중심 조준을 통한 대상 감지·선택을 담당한다.
[RequireComponent(typeof(PlayerInventory), typeof(PlayerHealth))]
public class PlayerInteraction : NetworkBehaviour
{
    // 상호작용 범위(SphereCollider) 안의 후보 목록과 중복 진입 카운트.
    private readonly HashSet<InteractableBase> _nearbyInteractables = new();
    private readonly Dictionary<InteractableBase, int> _overlapCounts = new();

    // 인스펙터에서 연결하는 참조와 상호작용 범위를 조절하는 값.
    [Header("상호작용 설정")]
    [SerializeField] private Camera _playerCamera;
    [Range(0.01f, 0.5f)]
    [SerializeField] private float _screenCenterRadius = 0.2f; // 화면 중심에서 상호작용 가능한 영역의 반지름
    [SerializeField] private InteractionPromptUI _promptUI;

    private InteractableBase _currentTarget;  // 현재 상호작용 가능한 대상

    private CustomInputActions _actions;
    private PlayerInventory _inventory;
    private PlayerHealth _health;
    private PlayerItemUse _itemUse;
    private HoldAction _activeHoldAction = HoldAction.None;
    // Interact및 아이템 사용 시 사용할 타이머 및 Threshold
    private float _holdTimer;
    private float _holdThreshold;
    private float _promptRefreshUntil; // 서버 상호작용 결과가 늦게 도착해도 안내 문구가 바로 바뀌도록 잠깐만 재확인한다.
    private InteractableBase _holdTarget;

    
    // 홀드 시 유형별로 어떤 조건을 체크해야 할지 여기서 나뉜다.
    private enum HoldAction
    {
        None,
        UseItem,
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
            _inventory.OnSlotSelected += HandleSlotSelected;
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
            _inventory.OnSlotSelected -= HandleSlotSelected;
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

    }

    private void HandleInteractInput()
    {
        // 대상을 조준 중이고 그 대상에 적용 가능한 IInteractionApplier 아이템을 들고 있으면 최우선으로 적용을 시도한다.
        if (_currentTarget != null && TryGetApplierForTarget(_currentTarget, out ItemBase applierItem, out _))
        {
            BeginHoldAction(HoldAction.ApplyItem, applierItem.ItemHoldThreshold, _currentTarget);
            return;
        }

        // 조준 중인 대상이 있으면 필드 상호작용을 우선한다. threshold가 0이면 BeginHoldAction 안에서 그 자리에 즉시 처리된다.
        if (_currentTarget != null)
        {
            BeginHoldAction(HoldAction.Interactable, _currentTarget.InteractHoldThreshold, _currentTarget);
            return;
        }
        // 선택한 아이템이 사용 가능하면 그 처리를 우선한다.
        if (_inventory != null && _inventory.TryGetSelectedItemBase(out ItemBase item) && item is IUsable usable)
        {
            if (usable.CanUse(gameObject, out string failReason))
            {
                BeginHoldAction(HoldAction.UseItem, item.ItemHoldThreshold);
                return;
            }

            if (failReason != null)
            {
                _promptUI?.ShowTemporaryPrompt(failReason);
                return;
            }
        }
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

    private void BeginHoldAction(HoldAction action, float holdThreshold, InteractableBase target = null)
    {
        _activeHoldAction = action;
        _holdTimer = 0f;
        _holdThreshold = holdThreshold;
        _holdTarget = target;

        // threshold가 0 이하면 즉시 처리한다.
        if (!(holdThreshold > 0f))
        {
            CompleteHoldAction();
            return;
        }

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
        float progress = Mathf.Clamp01(_holdTimer / _holdThreshold);
        _promptUI?.SetUseHoldProgress(progress, true);

        if (progress < 1f)
        {
            return;
        }

        CompleteHoldAction();
    }

    private bool IsHoldActionStillValid()
    {
        // holdAction은 선택한 아이템을 사용하거나, 조준한 대상과 상호작용하거나, 아이템을 투입할 때만 유지됩니다.
        return _activeHoldAction switch
        {
            // 아이템 사용 홀드시에는 타겟이 게속 없어야 함
            HoldAction.UseItem => _currentTarget == null,
            // Interact할 때에는 그 타겟이 바뀌면 안됨
            HoldAction.Interactable => _holdTarget != null && ReferenceEquals(_currentTarget, _holdTarget) && _holdTarget.CanInteract(gameObject),
            // 아이템을 사용해 물체와 상호작용할 때에는 그 물체를 계속 바라봐야함
            HoldAction.ApplyItem => _holdTarget != null && ReferenceEquals(_currentTarget, _holdTarget) && TryGetApplierForTarget(_holdTarget, out _, out _),
            _ => false
        };
    }

    private void CompleteHoldAction()
    {
        HoldAction completedAction = _activeHoldAction;
        InteractableBase completedTarget = _holdTarget;
        float completedThreshold = _holdThreshold;
        CancelHoldAction();

        switch (completedAction)
        {
            case HoldAction.UseItem:
                if (_itemUse != null && _itemUse.TryCompleteSelectedItemUse(out string failReason) && !string.IsNullOrWhiteSpace(failReason))
                {
                    _promptUI?.ShowTemporaryPrompt(failReason);
                }
                break;

            case HoldAction.Interactable:
                if (completedTarget != null && completedTarget.CanInteract(gameObject))
                {
                    completedTarget.Interact(gameObject);

                    // 실제로 길게 눌렀을 때만(threshold > 0) 재확인 구간을 연다.
                    if (completedThreshold > 0f)
                    {
                        _promptRefreshUntil = Time.time + 0.75f;
                        RefreshInteractionPrompt();
                    }
                }
                break;

            case HoldAction.ApplyItem:
                if (completedTarget != null && TryGetApplierForTarget(completedTarget, out ItemBase completedItem, out _))
                {
                    RequestApplyItemRpc(new NetworkBehaviourReference(completedItem), new NetworkBehaviourReference(completedTarget), _inventory.SelectedIndex);

                    if (completedTarget is MissionInteractable)
                    {
                        SoundManager.Instance?.Play(SoundKey.Mission_ItemInsert);
                    }

                    if (completedThreshold > 0f)
                    {
                        _promptRefreshUntil = Time.time + 0.75f;
                        RefreshInteractionPrompt();
                    }
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

        // ApplyToOnServer가 아이템을 소모(파괴)할 수 있으므로, 완료 메시지는 그 전에 미리 읽어둔다.
        string completedMessage = applier.ApplyCompletedMessage;

        applier.ApplyToOnServer(gameObject, target, _inventory, selectedIndex);

        HandleItemAppliedOwnerRpc(completedMessage, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void HandleItemAppliedOwnerRpc(string message, RpcParams rpcParams = default)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _promptUI?.ShowTemporaryPrompt(message);
        }
    }

    private void CancelHoldAction()
    {
        if (_activeHoldAction == HoldAction.None)
        {
            return;
        }

        _activeHoldAction = HoldAction.None;
        _holdTimer = 0f;
        _holdThreshold = 0f;
        _holdTarget = null;
        _promptUI?.SetUseHoldProgress(0f, false);
    }

    private void UpdateCartInteraction()
    {
        SetCurrentTarget(null);
        _promptUI.SetInteractionPrompt($"{CarryingCart.CartName}카트 놓기");

        if (_actions.Player.Interact.WasPressedThisFrame())
        {
            CarryingCart.ReleaseCart();
            _promptUI.SetInteractionPrompt("");
        }
    }

    // 상호작용 범위에 들어온 대상을 후보 목록에 추가한다.
    private void OnTriggerEnter(Collider other) // SphereCollider에 들어온 아이템을 nearbyInteractables에 추가
    {
        if (!IsOwner)
        {
            return;
        }

        if (IsWanderAreaCollider(other))
        {
            return; // NPC 배회 반경 콜라이더는 상호작용 판정 대상이 아니다
        }

        InteractableBase newTarget = other.GetComponentInParent<InteractableBase>();
        if (newTarget != null)
        {
            _overlapCounts.TryGetValue(newTarget, out int overlapCount);
            _overlapCounts[newTarget] = overlapCount + 1;
            _nearbyInteractables.Add(newTarget);
        }
    }

    // 범위를 벗어난 대상을 제거하고, 선택 중이었다면 선택도 해제한다.
    private void OnTriggerExit(Collider other)  // SphereCollider에서 나간 상호작용 대상을 nearbyInteractables에서 제거
    {
        if (!IsOwner)
        {
            return;
        }

        if (IsWanderAreaCollider(other))
        {
            return;
        }

        InteractableBase outTarget = other.GetComponentInParent<InteractableBase>();
        if (outTarget == null)
        {
            return;
        }

        if (_overlapCounts.TryGetValue(outTarget, out int overlapCount) && overlapCount > 1)
        {
            _overlapCounts[outTarget] = overlapCount - 1;
            return;
        }

        _overlapCounts.Remove(outTarget);
        _nearbyInteractables.Remove(outTarget);
        if (ReferenceEquals(outTarget, _currentTarget))
        {
            SetCurrentTarget(null);
        }
    }

    // NPC의 배회 반경 콜라이더인지 확인한다. 같은 오브젝트에 다른 콜라이더(몸체 등)가 있을 수 있으므로 참조까지 비교한다.
    private static bool IsWanderAreaCollider(Collider other)
    {
        return other.TryGetComponent(out NpcRandomWander wander) && wander.WanderAreaCollider == other;
    }

    // 선택 슬롯이 바뀌면(예: 스크롤로 아이템 선택/해제) 안내 문구를 바로 갱신한다.
    // 대상을 조준 중이 아니어도 들고 있는 IUsable 아이템 문구가 바뀔 수 있어 대상 유무와 상관없이 갱신한다.
    private void HandleInventoryChanged()
    {
        RefreshInteractionPrompt();
    }

    // 휠·숫자키로 슬롯을 바꾸면 손에 든 아이템 안내를 1초만 보여준다.
    // 사용할 수 있는 아이템이면 사용 문구를("단서 확인 : [E]"), 그 외에는 아이템 이름을 띄운다.
    // 이 안내는 우선순위가 가장 낮아서, 조준 중인 대상이 있으면 그 안내를 가리지 않는다.
    private void HandleSlotSelected(int selectedIndex)
    {
        if (_currentTarget != null || _health.IsDowned || CarryingCart != null || GameplayUiMode.IsActive)
        {
            return;
        }

        if (_inventory == null
            || !_inventory.TryGetSelectedItemBase(out ItemBase item)
            || item.ItemData == null)
        {
            return;
        }

        if (item is IUsable usable)
        {
            _promptUI?.ShowTemporaryPrompt(
                AppendUsageDescription(AppendHoldSuffix(usable.UseText, item.ItemHoldThreshold), item.ItemData),
                true);
            return;
        }

        // 사용 방법이 없는 아이템은 이름만 띄워봐야 알려 줄 게 없으므로 아예 표시하지 않는다.
        if (string.IsNullOrWhiteSpace(item.ItemData.UsageDescription))
        {
            return;
        }

        string selected = new LocalizedString(LocalizationTable, "interact_selected_item")
            .GetLocalizedString(item.ItemData.DisplayName);
        _promptUI?.ShowTemporaryPrompt(AppendUsageDescription(selected, item.ItemData));
    }

    // ItemData에 사용 방법이 적혀 있으면 안내 문구 뒤에 " : "로 이어 붙인다.
    private static string AppendUsageDescription(string message, ItemData itemData)
    {
        if (itemData == null || string.IsNullOrWhiteSpace(itemData.UsageDescription))
        {
            return message;
        }

        return $"{message} : {itemData.UsageDescription}";
    }

    // 현재 조준 대상과 들고 있는 아이템 기준으로 상호작용 안내 문구를 갱신한다.
    private void RefreshInteractionPrompt()
    {
        // 누워있거나, 카트 끌고 있거나, UI화성화중에는 SetInteractionPrompt 안띄우기
        if (_health.IsDowned || CarryingCart != null || GameplayUiMode.IsActive)
        {
            _promptUI?.SetInteractionPrompt(null, false);
            return;
        }

        // 1. 대상을 조준 중이고, 그 대상에 적용 가능한 IInteractionApplier 아이템을 들고 있으면 아이템 쪽 문구가 최우선.
        if (_currentTarget != null && TryGetApplierForTarget(_currentTarget, out ItemBase applierItem, out IInteractionApplier applier))
        {
            _promptUI?.SetInteractionPrompt(AppendHoldSuffix(applier.InteractionApplyText, applierItem.ItemHoldThreshold), true);
            return;
        }

        // 2. 대상만 조준 중이면 대상 자체 문구.
        if (_currentTarget != null)
        {
            string interactionText = AppendHoldSuffix(_currentTarget.GetInteractionText(gameObject), _currentTarget.InteractHoldThreshold);
            _promptUI?.SetInteractionPrompt(interactionText, _currentTarget.ShowInteractionKeyHint(gameObject));
            return;
        }

        // 들고 있는 아이템 안내는 슬롯을 고른 직후 1초만 띄운다(HandleSlotSelected). 상시로 띄우면
        // 조준 대상이 없는 동안 계속 남아 화면을 가린다.
        _promptUI?.SetInteractionPrompt(null, false);
    }

    // 길게 눌러야 하는 상호작용이면(threshold > 0) 안내 문구에 길게 누르기 표시를 붙인다.
    // 접미사를 이어 붙이지 않고 인자로 넘긴다. 언어에 따라 앞에 오거나 표현이 달라질 수 있다.
    private static string AppendHoldSuffix(string text, float holdThreshold)
    {
        if (holdThreshold <= 0f || string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return new LocalizedString(LocalizationTable, "interact_hold_suffix").GetLocalizedString(text);
    }

    private const string LocalizationTable = "Language Table";

    private void UpdateCurrentTarget()
    {
        if (_playerCamera == null)
        {
            SetCurrentTarget(null);
            return;
        }

        _nearbyInteractables.RemoveWhere(target => target == null); // 파괴된 대상 제거
        // 파괴된 대상을 정리한 뒤, 가장 가까운 후보를 찾는다.

        InteractableBase closestTarget = null;
        float closestDistanceSqr = float.MaxValue;

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        // 화면 중앙과의 거리를 기준으로 조준 대상을 비교한다.

        foreach (InteractableBase target in _nearbyInteractables)
        {
            if (target == null)
            {
                continue;
            }

            if (!target.CanInteract(gameObject))  // 상호작용이 차단된 대상은 무시
            {
                continue;
            }

            Vector3 screenPos = _playerCamera.WorldToScreenPoint(target.InteractionPosition);

            if (screenPos.z < 0)
            {
                continue; // 대상이 카메라 뒤에 있는 경우 무시
            }

            Vector2 targetScreenPos = new Vector2(screenPos.x, screenPos.y);

            // 화면 중심과 대상의 스크린 좌표 간의 거리 제곱 계산
            // 제곱 거리를 사용해 불필요한 제곱근 계산을 피한다.
            float distanceSqr = (targetScreenPos - screenCenter).sqrMagnitude;

            // 대상별 배율(AimRadiusMultiplier)을 반영해 판정 반경을 계산한다 (예: 계속 움직이는 NPC는 더 넓게).
            float radiusPixels = Screen.height * _screenCenterRadius * target.AimRadiusMultiplier;
            float radiusSqr = radiusPixels * radiusPixels;

            if (distanceSqr > radiusSqr)
            {
                continue; // 아이템이 상호작용 가능한 영역 밖에 있는 경우 무시
            }

            if (distanceSqr < closestDistanceSqr)
            {
                closestDistanceSqr = distanceSqr;
                closestTarget = target;
            }
        }
        SetCurrentTarget(closestTarget);
    }

    public void RemoveNearbyInteractable(InteractableBase target)
    {
        if (target == null)
        {
            return;
        }

        _nearbyInteractables.Remove(target);
        _overlapCounts.Remove(target);

        if (ReferenceEquals(_currentTarget, target))
        {
            SetCurrentTarget(null);
        }
    }

    private void SetCurrentTarget(InteractableBase nextTarget)
    {
        if (ReferenceEquals(_currentTarget, nextTarget)) {
            return;
        }

        // 이전에 선택된 대상의 아웃라인을 끈다.
        _currentTarget?.SetOutline(false);

        _currentTarget = nextTarget;
        _currentTarget?.SetOutline(true);

        RefreshInteractionPrompt();
    }
}
