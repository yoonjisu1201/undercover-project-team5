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

    // 인스펙터에서 연결하는 참조와 상호작용 범위를 조절하는 값.
    [Header("상호작용 설정")]
    [SerializeField] private Camera _playerCamera;
    [Range(0.01f, 0.5f)]
    [SerializeField] private float _screenCenterRadius = 0.2f; // 화면 중심에서 상호작용 가능한 영역의 반지름

    // 트리거 구체는 "얼마나 가까운가", 화면 중심 반경은 "어디를 보고 있나"만 판정한다.
    // 시야가 실제로 통하는지는 아무도 보지 않아서, 바닥 아래나 벽 뒤의 대상도 잡혔다.
    [Header("가려짐 판정")]
    [Tooltip("시야를 막는 것으로 볼 레이어. 좁히면 그 레이어는 대상을 가리지 않는다.")]
    [SerializeField] private LayerMask _occlusionBlockers = ~0;

    [Tooltip("조준점 앞 이 거리까지만 막힘을 본다. 조준점이 지오메트리 안쪽에 살짝 박혀 있는 "
        + "대상(벽 매립 패널 등)이 자기 벽에 막혀 못 잡히는 것을 피하기 위한 여유다.")]
    [SerializeField, Min(0f)] private float _occlusionTolerance = 0.25f;

    // 상호작용 범위(SphereCollider) 안의 후보 목록과 중복 진입 카운트.
    private readonly HashSet<InteractableBase> _nearbyInteractables = new();
    private readonly Dictionary<InteractableBase, int> _overlapCounts = new();

    // 매 프레임 도는 판정이라 할당을 남기지 않는다.
    private readonly List<InteractableBase> _pruneBuffer = new();
    private readonly RaycastHit[] _occlusionHits = new RaycastHit[16];

    // RequireComponent 가 보장하므로 null 검사를 하지 않는다. _itemUse 만 없을 수 있다.
    private PlayerInventory _inventory;
    private PlayerHealth _health;
    private PlayerItemUse _itemUse;
    private CustomInputActions _actions;

    // 안내 문구는 이 컴포넌트가 전담한다. 여기서는 무엇을 할 수 있는지만 알려준다.
    private InteractionPromptPresenter _prompt;

    private InteractableBase _currentTarget;  // 현재 상호작용 가능한 대상

    private HoldAction _activeHoldAction = HoldAction.None;
    private InteractableBase _holdTarget;
    private float _holdTimer;
    private float _holdThreshold;

    public CartBase CarryingCart { get; set; } // 플레이어가 끌고 있는 카트. null이면 카트를 끌고 있지 않다.

    // 지금 조준 중인 대상. 안내 문구가 이 값으로 무엇을 띄울지 정한다.
    public InteractableBase CurrentTarget => _currentTarget;

    // 쓰러졌거나 카트를 끌거나 UI 를 보는 중에는 조준·안내를 모두 접는다. 여러 곳에서 같은 조건을 물어본다.
    public bool IsInteractionBlocked => _health.IsDowned || CarryingCart != null || GameplayUiMode.IsActive;

    private void Awake()
    {
        _actions = new CustomInputActions();
        _inventory = GetComponent<PlayerInventory>();
        _health = GetComponent<PlayerHealth>();
        _itemUse = GetComponent<PlayerItemUse>();

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
        SetCurrentTarget(null);
        _actions?.Disable();
    }

    public override void OnNetworkSpawn()
    {
        // 입력과 UI는 이 플레이어를 조작하는 클라이언트에서만 초기화한다.
        if (!IsOwner)
        {
            _actions.Disable();
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
        _prompt.TickRefreshWindow();
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
        SetCurrentTarget(null);
        _prompt.SetStandingText($"{CarryingCart.CartName}카트 놓기");

        if (_actions.Player.Interact.WasPressedThisFrame())
        {
            CarryingCart.ReleaseCart();
            _prompt.SetStandingText("");
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

        // 확장 거리를 쓰는 대상(NPC)은 트리거를 벗어나도 후보로 남기고, 거리로만 정리한다.
        if (outTarget.ExtendedInteractionRange > 0f)
        {
            return;
        }

        ForgetInteractable(outTarget);
    }

    // NPC의 배회 반경 콜라이더인지 확인한다. 같은 오브젝트에 다른 콜라이더(몸체 등)가 있을 수 있으므로 참조까지 비교한다.
    private static bool IsWanderAreaCollider(Collider other)
    {
        return other.TryGetComponent(out NpcRandomWander wander) && wander.WanderAreaCollider == other;
    }

    private void UpdateCurrentTarget()
    {
        if (_playerCamera == null)
        {
            SetCurrentTarget(null);
            return;
        }

        PruneNearbyInteractables();

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

            // 1등이 될 수 없는 후보는 레이캐스트도 하지 않는다. 매 프레임 도는 판정이라
            // 가려짐 검사는 실제로 선택될 수 있는 후보에만 쓴다.
            if (distanceSqr >= closestDistanceSqr)
            {
                continue;
            }

            if (IsOccluded(target))
            {
                continue; // 벽·바닥에 가려진 대상은 조준되지 않는다
            }

            closestDistanceSqr = distanceSqr;
            closestTarget = target;
        }
        SetCurrentTarget(closestTarget);
    }

    // 후보 목록에서 더 볼 필요가 없는 대상을 걷어낸다.
    //
    // 비활성화까지 보는 이유: Unity 는 콜라이더가 비활성화될 때 OnTriggerExit 를 보내지 않는다.
    // UndergroundDoor 처럼 열린 뒤 SetActive(false) 하는 대상은 그대로 후보에 남는다.
    // target == null 은 파괴된 것만 걸러낸다.
    private void PruneNearbyInteractables()
    {
        _pruneBuffer.Clear();

        foreach (InteractableBase target in _nearbyInteractables)
        {
            if (target == null || !target.gameObject.activeInHierarchy || IsBeyondExtendedRange(target))
            {
                _pruneBuffer.Add(target);
            }
        }

        foreach (InteractableBase target in _pruneBuffer)
        {
            ForgetInteractable(target);
        }
    }

    // 후보 목록에서 완전히 잊는다. 겹침 카운트도 같이 지워야 한다 — 남겨두면 트리거 안에서 다시
    // 켜질 때 OnTriggerEnter 가 또 와서 카운트가 실제 겹침보다 커지고, 그러면 트리거를 나가도
    // 목록에서 빠지지 않는다.
    private void ForgetInteractable(InteractableBase target)
    {
        _nearbyInteractables.Remove(target);

        // 파괴된 대상은 Unity 의 == 로는 null 이지만 참조는 살아 있어 키로 쓸 수 있다.
        // 진짜 null 만 걸러낸다 (Dictionary.Remove 는 null 키에 예외를 던진다).
        if (!ReferenceEquals(target, null))
        {
            _overlapCounts.Remove(target);
        }

        if (ReferenceEquals(_currentTarget, target))
        {
            SetCurrentTarget(null);
        }
    }

    // 카메라에서 조준점까지 시야가 막혀 있는지.
    //
    // BossPerception.HasLineOfSight 처럼 "가장 가까운 히트가 표적인가"를 따지지 않아도 된다.
    // 레이를 조준점 앞까지만 쏘고 자기 몸과 표적 자신의 콜라이더만 걸러내면, 남은 히트는
    // 전부 표적을 가리는 것이다.
    private bool IsOccluded(InteractableBase target)
    {
        Vector3 eye = _playerCamera.transform.position;
        Vector3 direction = target.InteractionPosition - eye;
        float distance = direction.magnitude;
        float rayLength = distance - _occlusionTolerance;

        if (rayLength <= 0.01f)
        {
            return false; // 조준점이 눈앞이면 막힐 구간이 없다
        }

        int count = Physics.RaycastNonAlloc(
            eye,
            direction / distance,
            _occlusionHits,
            rayLength,
            _occlusionBlockers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = _occlusionHits[i].collider;

            if (hit == null)
            {
                continue;
            }

            // 카메라가 내 몸 안에 있어서 내 콜라이더가 잡힌다.
            if (hit.transform.IsChildOf(transform))
            {
                continue;
            }

            // 표적 자신의 콜라이더는 도착한 것이지 막은 것이 아니다.
            if (hit.GetComponentInParent<InteractableBase>() == target)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    // 트리거를 벗어난 뒤에도 남겨둔 대상이 확장 거리까지 벗어났는지.
    private bool IsBeyondExtendedRange(InteractableBase target)
    {
        // 아직 트리거 안에 있으면 거리로 빼지 않는다. 다시 넣어 줄 OnTriggerEnter가 오지 않아
        // 영구히 상호작용 불가가 되기 때문이다.
        if (_overlapCounts.ContainsKey(target))
        {
            return false;
        }

        float range = target.ExtendedInteractionRange;
        return range > 0f
            && (target.InteractionPosition - transform.position).sqrMagnitude > range * range;
    }

    // 인벤토리에 들어간 아이템처럼, 트리거를 벗어나지 않고도 후보에서 빠져야 하는 경우에 부른다.
    public void RemoveNearbyInteractable(InteractableBase target)
    {
        if (target == null)
        {
            return;
        }

        ForgetInteractable(target);
    }

    private void SetCurrentTarget(InteractableBase nextTarget)
    {
        if (ReferenceEquals(_currentTarget, nextTarget))
        {
            return;
        }

        // 이전에 선택된 대상의 아웃라인을 끈다.
        _currentTarget?.SetOutline(false);

        _currentTarget = nextTarget;
        _currentTarget?.SetOutline(true);

        _prompt.Refresh();
    }
}
