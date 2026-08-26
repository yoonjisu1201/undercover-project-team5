using Unity.Netcode;
using UnityEngine;

// 다운된 플레이어를 다른 플레이어가 상호작용으로 소생시킬 수 있게 만드는 스크립트.
// 이 컴포넌트는 다운된 사람 자신의 오브젝트에 붙는다 (ArrestCandidateInteractable이 NPC에 붙는 것과 같은 구조).
public class PlayerReviveInteractable : InteractableBase
{
    // 서버 재검증 시 RPC 왕복 지연으로 인한 위치 오차를 흡수하기 위한 여유 거리.
    [SerializeField, Min(0f)] private float _rangeTolerance = 2f;

    [SerializeField, Min(0f)] private float _reviveHoldDuration = 1.2f;

    [Header("쓰러진 상태 조준")]
    [Tooltip("쓰러졌을 때 조준점으로 쓸 위치. 몸을 따라가는 본(Hips)을 넣는다.")]
    [SerializeField] private Transform _downedAimAnchor;

    [Tooltip("쓰러진 대상은 바닥에 넓게 누워 있어 조준 판정 반경을 넓혀 준다.")]
    [SerializeField, Min(1f)] private float _downedAimRadiusMultiplier = 1.6f;

    [Tooltip("소생 가능 거리. 공용 상호작용 트리거(반지름 2.5)는 모든 대상이 함께 쓰므로 소생만 따로 좁힌다.")]
    [SerializeField, Min(0.5f)] private float _maxReviveDistance = 1.6f;

    public override float InteractHoldThreshold => _reviveHoldDuration;

    // 몸 콜라이더는 쓰러져도 서 있는 크기(높이 2, 중심 y 0.97) 그대로라, 기본 조준점은
    // 바닥에 누운 몸보다 1m쯤 위에 뜬다. 그래서 몸을 내려다보면 조준이 잡히지 않는다.
    // 쓰러진 동안은 실제 몸을 따라가는 본 위치를 조준점으로 쓴다.
    public override Vector3 InteractionPosition
    {
        get
        {
            if (_downedAimAnchor != null && TryGetComponent(out PlayerHealth health) && health.IsDowned)
            {
                return _downedAimAnchor.position;
            }

            return base.InteractionPosition;
        }
    }

    // 바닥에 누운 대상은 화면에서 가로로 길게 퍼져 보이므로 판정을 조금 넉넉하게 잡는다.
    public override float AimRadiusMultiplier =>
        TryGetComponent(out PlayerHealth health) && health.IsDowned
            ? _downedAimRadiusMultiplier
            : base.AimRadiusMultiplier;

    public override bool CanInteract(GameObject interactor)
    {
        return IsSpawned
            && interactor != gameObject
            && TryGetComponent(out PlayerHealth health)
            && health.IsDowned
            && Vector3.Distance(interactor.transform.position, transform.position) <= _maxReviveDistance;
    }

    public override void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor))
        {
            return;
        }

        RequestReviveRpc();
    }

    // 이 오브젝트의 소유자는 다운된 사람이지만, RPC를 호출하는 건 소생시키는 다른 플레이어다.
    // 기본값(소유자만 호출 가능)으로는 막히므로 Everyone으로 열어둔다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestReviveRpc(RpcParams rpcParams = default)
    {
        if (!IsSpawned || !TryGetComponent(out PlayerHealth health) || !health.IsDowned)
        {
            return;
        }

        // NpcInteractionValidation은 이름과 달리 NPC 전용이 아니라 "요청자의 위치 재검증" 범용 유틸이라 여기서도 재사용한다.
        if (!NpcInteractionValidation.TryGetInteractionCollider(NetworkManager, rpcParams.Receive.SenderClientId, out SphereCollider interactionCollider) ||
            !NpcInteractionValidation.IsWithinInteractionRange(transform.position, interactionCollider, _rangeTolerance))
        {
            return;
        }

        health.Revive();
    }
}
