using System;
using System.Collections.Generic;
using UnityEngine;

// 상호작용 대상을 찾고 그중 하나를 고르는 책임만 가진다. PlayerInteraction 에서 떼어냈다.
//
// 한 문장으로: "범위 안의 후보를 모아, 그중 겨누고 있고 실제로 보이는 하나를 고른다."
// 감지와 선택을 굳이 두 클래스로 나누지 않은 이유는, 후보 집합을 읽는 곳이 선택 로직 하나뿐이고
// 가려짐 판정이 두 단계에 이미 걸쳐 있기 때문이다.
//
// 판정은 세 단계이고 서로 다른 질문에 답한다.
//   1) 트리거 구체  - 얼마나 가까운가        (물리 엔진이 공짜로 해주는 공간 분할)
//   2) 화면 중심 반경 - 어디를 보고 있나
//   3) 레이캐스트     - 실제로 보이는가
//
// 비오너에서는 컴포넌트가 꺼진 채로 있어서 트리거 콜백도 오지 않는다(PlayerInteraction 이 켠다).
public sealed class InteractionTargeting : MonoBehaviour
{
    [Header("조준")]
    [SerializeField] private Camera _playerCamera;
    [Range(0.01f, 0.5f)]
    [SerializeField] private float _screenCenterRadius = 0.2f; // 화면 중심에서 상호작용 가능한 영역의 반지름

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

    private InteractableBase _currentTarget;

    // 조준 대상이 바뀐 순간. 안내 문구가 구독한다. 여기서는 문구를 만들지 않는다.
    public event Action TargetChanged;

    public InteractableBase CurrentTarget => _currentTarget;

    // 조준 대상을 다시 고른다. 프레임마다 PlayerInteraction 이 부른다.
    public void UpdateTarget()
    {
        if (_playerCamera == null)
        {
            SetCurrentTarget(null);
            return;
        }

        PruneNearbyInteractables();

        InteractableBase closestTarget = null;
        float closestDistanceSqr = float.MaxValue;

        // 화면 중앙과의 거리를 기준으로 조준 대상을 비교한다.
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

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

            if (!TryGetAimDistances(target, screenCenter, out float boundsDistanceSqr, out float centerDistanceSqr))
            {
                continue; // 카메라 뒤에 있는 경우 무시
            }

            // 대상별 배율(AimRadiusMultiplier)을 반영해 판정 반경을 계산한다 (예: 계속 움직이는 NPC는 더 넓게).
            float radiusPixels = Screen.height * _screenCenterRadius * target.AimRadiusMultiplier;

            if (boundsDistanceSqr > radiusPixels * radiusPixels)
            {
                continue; // 조준 가능한 영역 밖
            }

            // 1등이 될 수 없는 후보는 레이캐스트도 하지 않는다. 매 프레임 도는 판정이라
            // 가려짐 검사는 실제로 선택될 수 있는 후보에만 쓴다.
            if (centerDistanceSqr >= closestDistanceSqr)
            {
                continue;
            }

            if (IsOccluded(target))
            {
                continue; // 벽·바닥에 가려진 대상은 조준되지 않는다
            }

            closestDistanceSqr = centerDistanceSqr;
            closestTarget = target;
        }

        SetCurrentTarget(closestTarget);
    }

    // 조준선이 대상에서 얼마나 벗어났는지(제곱 픽셀)를 두 가지로 잰다. 하나로 합치지 않는 이유는
    // "겨눌 수 있는가"와 "여럿 중 어느 쪽인가"가 서로 다른 값을 요구하기 때문이다.
    //
    // boundsDistanceSqr — 대상 바운드를 화면에 투영한 사각형까지의 거리. 판정 반경 안인지만 본다.
    // 조준점 한 점으로 반경을 재면 문처럼 큰 대상에서 어긋난다. 조준점은 콜라이더 바운드의 중심이라,
    // 눈에 보이는 문짝 아무 곳을 겨눠도 그 중심이 화면 중앙에서 멀면 반경 밖으로 밀려난다.
    //
    // centerDistanceSqr — 조준점까지의 거리. 후보끼리의 순위는 이 값으로 매긴다.
    // 사각형 거리로 순위까지 매기면 큰 대상이 가까이서 화면을 덮어 어디를 겨눠도 0 이 되고,
    // 그 안이나 앞에 놓인 작은 대상을 전부 삼킨다 (브레이커 패널이 그 위의 레버를 가져가는 식).
    //
    // 조준점이 카메라 뒤라 투영이 안 되면 사각형 거리로 순위를 대신한다. 그런 대상은 눈앞에 걸쳐 있다.
    private bool TryGetAimDistances(InteractableBase target, Vector2 screenCenter,
        out float boundsDistanceSqr, out float centerDistanceSqr)
    {
        boundsDistanceSqr = float.MaxValue;
        centerDistanceSqr = float.MaxValue;

        Vector3 pointScreen = _playerCamera.WorldToScreenPoint(target.InteractionPosition);
        bool hasCenter = pointScreen.z >= 0f;

        if (hasCenter)
        {
            centerDistanceSqr = ((Vector2)pointScreen - screenCenter).sqrMagnitude;
            boundsDistanceSqr = centerDistanceSqr;
        }

        if (!TryGetScreenRect(target, out Rect rect))
        {
            return hasCenter;
        }

        // Rect 밖이면 각 축으로 벗어난 만큼, 안이면 0.
        float dx = Mathf.Max(rect.xMin - screenCenter.x, 0f, screenCenter.x - rect.xMax);
        float dy = Mathf.Max(rect.yMin - screenCenter.y, 0f, screenCenter.y - rect.yMax);
        float rectDistanceSqr = dx * dx + dy * dy;

        boundsDistanceSqr = Mathf.Min(boundsDistanceSqr, rectDistanceSqr);

        if (!hasCenter)
        {
            centerDistanceSqr = rectDistanceSqr;
        }

        return true;
    }

    // 대상 바운드 여덟 꼭짓점을 화면에 투영해 감싸는 사각형. 하나라도 카메라 뒤면 포기한다 —
    // 뒤에 있는 점은 투영이 뒤집혀서 엉뚱하게 큰 사각형이 나온다.
    private bool TryGetScreenRect(InteractableBase target, out Rect rect)
    {
        rect = default;

        if (!target.TryGetInteractionBounds(out Bounds bounds))
        {
            return false;
        }

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) == 0 ? min.x : max.x,
                (i & 2) == 0 ? min.y : max.y,
                (i & 4) == 0 ? min.z : max.z);

            Vector3 screen = _playerCamera.WorldToScreenPoint(corner);

            if (screen.z < 0f)
            {
                return false;
            }

            xMin = Mathf.Min(xMin, screen.x);
            xMax = Mathf.Max(xMax, screen.x);
            yMin = Mathf.Min(yMin, screen.y);
            yMax = Mathf.Max(yMax, screen.y);
        }

        rect = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        return true;
    }

    public void ClearTarget()
    {
        SetCurrentTarget(null);
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

    // 상호작용 범위에 들어온 대상을 후보 목록에 추가한다.
    private void OnTriggerEnter(Collider other)
    {
        if (IsWanderAreaCollider(other))
        {
            return; // NPC 배회 반경 콜라이더는 상호작용 판정 대상이 아니다
        }

        InteractableBase newTarget = other.GetComponentInParent<InteractableBase>();

        if (newTarget == null)
        {
            return;
        }

        _overlapCounts.TryGetValue(newTarget, out int overlapCount);
        _overlapCounts[newTarget] = overlapCount + 1;
        _nearbyInteractables.Add(newTarget);
    }

    // 범위를 벗어난 대상을 제거하고, 선택 중이었다면 선택도 해제한다.
    private void OnTriggerExit(Collider other)
    {
        if (IsWanderAreaCollider(other))
        {
            return;
        }

        InteractableBase outTarget = other.GetComponentInParent<InteractableBase>();

        if (outTarget == null)
        {
            return;
        }

        // 같은 대상의 콜라이더가 여러 개면 마지막 하나가 빠질 때만 후보에서 지운다.
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

    // 카메라에서 조준점까지 시야가 막혀 있는지.
    //
    // BossPerception.HasLineOfSight 처럼 "가장 가까운 히트가 표적인가"를 따지지 않아도 된다.
    // 레이를 조준점 앞까지만 쏘고 자기 몸과 표적 자신의 콜라이더만 걸러내면, 남은 히트는
    // 전부 표적을 가리는 것이다.
    private bool IsOccluded(InteractableBase target)
    {
        Vector3 eye = _playerCamera.transform.position;
        Vector3 aimPoint = target.InteractionPosition;
        Vector3 direction = aimPoint - eye;
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

            // 표적을 품고 있는 콜라이더도 막은 것이 아니다. 선반·캐비닛처럼 속이 빈 프롭에
            // 박스 콜라이더 하나만 씌워 두면, 그 안에 놓인 아이템은 눈에 다 보이는데도
            // 프롭 앞면에 막혀 영원히 잡히지 않는다 (본부 선반의 제압기가 그랬다).
            if (ContainsPoint(hit, aimPoint))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    // point 가 콜라이더 내부인지. ClosestPoint 는 내부의 점을 그대로 돌려준다.
    //
    // 오목 MeshCollider 는 안팎을 구분하지 못해 밖의 점도 그대로 돌려주므로 이 판정에 쓸 수 없다.
    // 건물·지형이 그런 경우라, 판정할 수 없으면 계속 가리는 것으로 두는 편이 안전하다.
    private static bool ContainsPoint(Collider collider, Vector3 point)
    {
        if (collider is MeshCollider mesh && !mesh.convex)
        {
            return false;
        }

        return (collider.ClosestPoint(point) - point).sqrMagnitude < 0.0001f;
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

        TargetChanged?.Invoke();
    }
}
