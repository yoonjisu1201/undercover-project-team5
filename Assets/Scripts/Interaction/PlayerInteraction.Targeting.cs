using System.Collections.Generic;
using UnityEngine;

// 화면 중심 조준으로 상호작용 대상을 감지하고 가장 가까운 후보를 선택한다.
public partial class PlayerInteraction
{
    // 상호작용 범위(SphereCollider) 안의 후보 목록과 중복 진입 카운트.
    private readonly HashSet<InteractableBase> _nearbyInteractables = new();
    private readonly Dictionary<InteractableBase, int> _overlapCounts = new();

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

    // 현재 조준 대상과 들고 있는 아이템 기준으로 상호작용 안내 문구를 갱신한다.
    // 우선순위: 2) 대상에 투입 가능한 IInteractionApplier 아이템 > 1) 대상 자체 문구 > 3) 대상 없을 때 IUsable 아이템 문구.
    // 다운/카트/GameplayUiMode 중엔 상호작용 자체가 막혀 있으므로, 이 4가지 호출부(SetCurrentTarget,
    // HandleInventoryChanged, Update의 재확인 창, CompleteHoldAction)가 전부 여길 거치는 김에 여기서 한 번만 막는다.
    private void RefreshInteractionPrompt()
    {
        if (_health.IsDowned || CarryingCart != null || GameplayUiMode.IsActive)
        {
            _promptUI?.SetInteractionPrompt(null, false);
            return;
        }

        // 2. 대상을 조준 중이고, 그 대상에 적용 가능한 IInteractionApplier 아이템을 들고 있으면 아이템 쪽 문구가 최우선.
        if (_currentTarget != null && TryGetApplierForTarget(_currentTarget, out _, out IInteractionApplier applier))
        {
            _promptUI?.SetInteractionPrompt($"{applier.InteractionApplyText} (길게 누르기)", true);
            return;
        }

        // 1. 대상만 조준 중이면 대상 자체 문구.
        if (_currentTarget != null)
        {
            string interactionText = _currentTarget.GetInteractionText(gameObject);

            if (_currentTarget.RequiresHoldInteraction(gameObject) && !string.IsNullOrWhiteSpace(interactionText))
            {
                interactionText = $"{interactionText} (길게 누르기)";
            }

            _promptUI?.SetInteractionPrompt(interactionText, _currentTarget.ShowInteractionKeyHint(gameObject));
            return;
        }

        // 3. 조준 대상이 없고 IUsable 아이템을 들고 있으면 아이템 문구.
        if (_inventory != null && _inventory.TryGetSelectedItemBase(out ItemBase usableItem) && usableItem is IUsable usable)
        {
            string interactionText = usable.RequiresHold ? $"{usable.UseText} (길게 누르기)" : usable.UseText;
            _promptUI?.SetInteractionPrompt(interactionText, true);
            return;
        }

        _promptUI?.SetInteractionPrompt(null, false);
    }

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

        RefreshInteractionPrompt();
    }
}
