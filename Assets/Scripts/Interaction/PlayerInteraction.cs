using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 플레이어 상호작용: 참조/라이프사이클, 프레임별 입력 분배, 화면 중심 조준을 통한 대상 감지·선택을 담당한다.
[RequireComponent(typeof(PlayerInventory), typeof(PlayerHealth))]
public class PlayerInteraction : NetworkBehaviour
{
    // 홀드 시 유형별로 어떤 조건을 체크해야 할지 여기서 나뉜다.
    private enum HoldAction
    {
        None,
        UseItem,
        Interactable,
        ApplyItem
    }

    // PlayerInventory와 PlayerHealth는 RequireComponent가 보장한다. PlayerItemUse는 선택 구성이라 null-safe로 사용한다.
    private PlayerInventory _inventory;
    private PlayerHealth _health;
    private PlayerItemUse _itemUse;
    // #803: 동기화된 착석 단계와 현재 좌석을 조회해 착석 중 입력을 해당 좌석 상호작용으로 전달한다.
    private PlayerMoveSample _playerMove;
    private CustomInputActions _actions;

    // 대상 감지·조준과 안내 문구는 각각 전담 컴포넌트가 맡는다.
    // 여기서는 언제 조준을 갱신할지, 무엇을 실행할지만 정한다.
    private InteractionTargeting _targeting;
    private InteractionPromptPresenter _prompt;

    private HoldAction _activeHoldAction = HoldAction.None;
    private InteractableBase _holdTarget;
    private float _holdTimer;
    private float _holdThreshold;

    public CartBase CarryingCart { get; set; } // 플레이어가 끌고 있는 카트. null이면 카트를 끌고 있지 않다.

    // 지금 조준 중인 대상. 안내 문구가 이 값으로 무엇을 띄울지 정한다.
    public InteractableBase CurrentTarget => _targeting.CurrentTarget;

    // 쓰러졌거나 카트를 끌거나 UI 를 보는 중에는 조준·안내를 모두 접는다. 여러 곳에서 같은 조건을 물어본다.
    // #803: 착석 흐름 중에는 일반 조준을 막고 현재 좌석 안내와 입력만 별도 경로에서 처리한다.
    public bool IsInteractionBlocked =>
        _health.IsDowned ||
        CarryingCart != null ||
        GameplayUiMode.IsActive ||
        _playerMove.IsSitting;

    private void Awake()
    {
        _actions = new CustomInputActions();
        _inventory = GetComponent<PlayerInventory>();
        _health = GetComponent<PlayerHealth>();
        _itemUse = GetComponent<PlayerItemUse>();
        _playerMove = GetComponent<PlayerMoveSample>();

        _targeting = GetComponent<InteractionTargeting>();

        if (_targeting == null)
        {
            _targeting = gameObject.AddComponent<InteractionTargeting>();
        }

        // 조준 대상이 바뀌면 문구가 따라 바뀐다. 여기서 두 컴포넌트를 이어준다.
        _targeting.TargetChanged += OnTargetChanged;

        // 안내 문구 컴포넌트는 RequireComponent 로 걸지 않는다. 그러면 이 스크립트가 붙어 있는
        // 옛 캐릭터 프리팹들(Player_T 등, 지금은 쓰지 않는다)에도 에디터가 자동으로 붙이며
        // 로그를 남긴다. 실제로 쓰는 프리팹에는 붙여 두었고, 없으면 여기서 만들어 붙인다.
        // 어느 경로든 null 이 아니므로 아래에서 null 검사를 하지 않는다.
        _prompt = GetComponent<InteractionPromptPresenter>();

        if (_prompt == null)
        {
            _prompt = gameObject.AddComponent<InteractionPromptPresenter>();
        }
    }

    private void OnEnable()
    {
        _actions ??= new CustomInputActions();
        _actions.Enable();
    }

    private void OnDisable()
    {
        _targeting.ClearTarget();
        _actions?.Disable();
    }

    public override void OnNetworkSpawn()
    {
        // 입력과 UI는 이 플레이어를 조작하는 클라이언트에서만 초기화한다.
        if (!IsOwner)
        {
            // 컴포넌트를 꺼두면 트리거 콜백조차 오지 않는다. 예전에는 콜백 안에서 IsOwner 를
            // 매번 확인했는데, 애초에 받지 않는 편이 분명하다.
            _actions.Disable();
            _targeting.enabled = false;
            return;
        }

        InitializeOnGameScene();

        _prompt.SubscribeInventory();
    }

    // 대기방에서 스폰된 채로 게임씬까지 파괴되지 않고 유지되는 플레이어 오브젝트는
    // OnNetworkSpawn이 대기방에서 한 번만 실행되어 게임씬 전용 UI를 못 찾는다.
    // 게임씬 로드가 끝난 뒤 GameSessionManager가 이 함수만 다시 호출해 UI 참조를 갱신한다
    // (이벤트 재구독까지 같이 도는 OnNetworkSpawn() 전체 재호출은 이중 구독을 유발하므로 피한다).
    public void InitializeOnGameScene()
    {
        _itemUse?.BindInteractionPromptUI(_prompt.ResolveUi());
    }

    public override void OnNetworkDespawn()
    {
        _prompt.UnsubscribeInventory();
    }

    private void OnDestroy()
    {
        if (_targeting != null)
        {
            _targeting.TargetChanged -= OnTargetChanged;
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
            _targeting.ClearTarget();
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
            _targeting.ClearTarget();
            return;
        }

        // #803: 착석 처리를 일반 조준·홀드 갱신보다 먼저 끝내 좌석 외 상호작용이 함께 실행되지 않게 한다.
        if (TryHandleSittingInteraction())
        {
            return;
        }

        _targeting.UpdateTarget();
        _prompt.TickRefreshWindow();
        UpdateHoldAction();

        // 조준 대상을 갱신한 뒤 상호작용과 드롭 입력을 처리한다.
        if (_actions.Player.Interact.WasPressedThisFrame()) // 상호작용 버튼이 눌렸을 때
        {
            HandleInteractInput();
        }

    }

    private bool TryHandleSittingInteraction()
    {
        if (!_playerMove.IsSitting)
        {
            return false;
        }

        // #803: 착석 중에는 조준 중이던 대상과 홀드 입력을 유지하지 않고 동기화된 현재 좌석만 사용한다.
        CancelHoldAction();
        _targeting.ClearTarget();

        // #803: 좌석이 비활성화되거나 아직 등록되지 않았어도 일반 상호작용으로 입력이 새지 않도록 소비한다.
        if (!_playerMove.TryGetCurrentSeatInteractable(out InteractableBase currentSeat))
        {
            _prompt.Clear();
            return true;
        }

        // #803: 좌석이 결정한 문구와 키 힌트를 기존 Presenter 표시 메서드에 전달한다.
        _prompt.SetStandingText(currentSeat.GetInteractionText(gameObject), currentSeat.ShowInteractionKeyHint(gameObject));

        // #803: 전환 중에는 좌석 CanInteract가 false이므로 완전히 착석한 경우에만 같은 Interact 경로로 기상한다.
        if (_actions.Player.Interact.WasPressedThisFrame() && currentSeat.CanInteract(gameObject))
        {
            currentSeat.Interact(gameObject);
        }

        return true;
    }

    private void HandleInteractInput()
    {
        // 대상을 조준 중이고 그 대상에 적용 가능한 IInteractionApplier 아이템을 들고 있으면 최우선으로 적용을 시도한다.
        if (CurrentTarget != null && TryGetApplierForTarget(CurrentTarget, out ItemBase applierItem, out _))
        {
            BeginHoldAction(HoldAction.ApplyItem, applierItem.ItemHoldThreshold, CurrentTarget);
            return;
        }

        // 조준 중인 대상이 있으면 필드 상호작용을 우선한다. threshold가 0이면 BeginHoldAction 안에서 그 자리에 즉시 처리된다.
        if (CurrentTarget != null)
        {
            BeginHoldAction(HoldAction.Interactable, CurrentTarget.InteractHoldThreshold, CurrentTarget);
            return;
        }
        // 선택한 아이템이 사용 가능하면 그 처리를 우선한다.
        if (_inventory.TryGetSelectedItemBase(out ItemBase item) && item is IUsable usable)
        {
            if (usable.CanUse(gameObject, out string failReason))
            {
                BeginHoldAction(HoldAction.UseItem, item.ItemHoldThreshold);
                return;
            }

            if (failReason != null)
            {
                _prompt.ShowTemporary(failReason);
                return;
            }
        }
    }

    // 현재 선택한 아이템이 target에 적용 가능한 IInteractionApplier인지 확인한다.
    // 현재 선택한 아이템이 target 에 적용 가능한 IInteractionApplier 인지 확인한다.
    // 입력 우선순위와 안내 문구가 같은 판정을 쓴다.
    public bool TryGetApplierForTarget(InteractableBase target, out ItemBase item, out IInteractionApplier applier)
    {
        item = null;
        applier = null;

        if (target == null
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
        if (holdThreshold <= 0f)
        {
            CompleteHoldAction();
            return;
        }

        _prompt.SetHoldProgress(0f, true);
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
        _prompt.SetHoldProgress(progress, true);

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
            HoldAction.UseItem => CurrentTarget == null,
            // Interact할 때에는 그 타겟이 바뀌면 안됨
            HoldAction.Interactable => _holdTarget != null && ReferenceEquals(CurrentTarget, _holdTarget) && _holdTarget.CanInteract(gameObject),
            // 아이템을 사용해 물체와 상호작용할 때에는 그 물체를 계속 바라봐야함
            HoldAction.ApplyItem => _holdTarget != null && ReferenceEquals(CurrentTarget, _holdTarget) && TryGetApplierForTarget(_holdTarget, out _, out _),
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
                if (_itemUse != null && _itemUse.TryCompleteSelectedItemUse(out string failReason))
                {
                    _prompt.ShowTemporary(failReason);
                }
                break;

            case HoldAction.Interactable:
                if (completedTarget != null && completedTarget.CanInteract(gameObject))
                {
                    completedTarget.Interact(gameObject);

                    _prompt.OpenRefreshWindow(completedThreshold);
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

                    _prompt.OpenRefreshWindow(completedThreshold);
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
        _prompt.ShowTemporary(message);
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
        _prompt.SetHoldProgress(0f, false);
    }

    private void UpdateCartInteraction()
    {
        _targeting.ClearTarget();
        _prompt.SetStandingText($"{CarryingCart.CartName}카트 놓기");

        if (_actions.Player.Interact.WasPressedThisFrame())
        {
            CarryingCart.ReleaseCart();
            _prompt.SetStandingText("");
        }
    }

    // 조준 대상이 바뀌면 안내 문구를 다시 정한다.
    private void OnTargetChanged()
    {
        _prompt.Refresh();
    }
}
