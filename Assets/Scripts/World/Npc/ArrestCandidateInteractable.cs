using Unity.Netcode;
using UnityEngine;

public class ArrestCandidateInteractable : InteractableBase
{
    public override string InteractionText => "검거 후보로 지정?";

    // 추적기를 선택 중일 때는 검거 후보 지정 문구 대신 부착 안내/이미 부착됨 안내를 보여준다.
    public override string GetInteractionText(GameObject interactor)
    {
        if (!TryGetComponent(out NpcTracker tracker) || !tracker.IsTrackerItemSelected(interactor))
        {
            return InteractionText;
        }

        return tracker.IsTracked ? "이미 위치추적중인 시민입니다." : "위치 추적기 부착";
    }

    // 이미 부착된 NPC에 추적기를 선택 중일 때는 눌러도 아무 동작이 없으므로 키 힌트를 보여주지 않는다.
    public override bool ShowInteractionKeyHint(GameObject interactor)
    {
        return !TryGetComponent(out NpcTracker tracker) || !tracker.IsTrackerItemSelected(interactor) || !tracker.IsTracked;
    }

    // 이미 다른 대상으로 투표가 진행 중이거나 추격전이 진행 중이면 새 후보를 지정할 수 없다.
    public override bool CanInteract(GameObject interactor)
    {
        return ArrestVoteManager.Instance != null
            && ArrestVoteManager.Instance.CurrentVoteState == ArrestVoteState.Idle
            && (ArrestChaseManager.Instance == null || ArrestChaseManager.Instance.CurrentState == ArrestChaseState.Idle);
    }

    // NPC는 계속 움직이므로 조준 판정 반경을 넉넉하게 잡는다.
    [SerializeField, Min(1f)] private float _aimRadiusMultiplier = 3f;
    public override float AimRadiusMultiplier => _aimRadiusMultiplier;

    // 서버 재검사 시 네트워크 지연으로 인한 위치 오차를 흡수하기 위한 여유 거리.
    // NPC는 RPC 왕복 시간(상호작용 → 서버 처리) 동안에도 계속 이동하므로,
    // 그 사이 이동 가능한 거리를 여유 있게 흡수할 수 있는 값으로 잡는다.
    [SerializeField, Min(0f)] private float _rangeTolerance = 2f;

    public override void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor) || !IsSpawned) {
            return;
        }

        // 추적기를 선택 중이면 검거 후보 지정 대신 부착(또는 이미 부착됨 안내)만 처리하고 끝낸다.
        if (TryGetComponent(out NpcTracker tracker) && tracker.IsTrackerItemSelected(interactor))
        {
            if (tracker.CanAttach(interactor))
            {
                tracker.RequestAttach();
            }
            return;
        }

        // 확인 패널을 여는 동안 NPC가 멀어져서 재검사에 실패하지 않도록, 상호작용 시점에 바로 멈춰둔다.
        RequestPauseForConfirmationRpc();
        FindFirstObjectByType<ArrestVoteUI>()?.RequestOpenStartVotePanel(this);
    }

    // 확인 패널에서 [아니요]를 누르거나 패널을 닫았을 때 ArrestVoteUI가 호출하는 진입점.
    public void CancelPendingConfirmation()
    {
        RequestResumeAfterCancelRpc();
    }

    // 확인 패널에서 [네]를 눌렀을 때 ArrestVoteUI가 호출하는 진입점.
    public void ConfirmArrestCandidate()
    {
        RequestSelectArrestCandidateRpc();
    }

    //--- 서버에서 검거 후보 지정 요청을 재검증하는 Rpc 관련 코드 ---//
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestPauseForConfirmationRpc(RpcParams rpcParams = default)
    {
        // 확인 패널이 실제로 열릴 수 없는 상황(투표 진행 중, 횟수 소진)이면 멈추지도 않는다.
        if (!IsSpawned || !CanInteract(gameObject) || ArrestVoteManager.Instance.RemainingVoteAttempts <= 0) {
            return;
        }

        // 거리 재검증 로직은 NpcTracker(위치추적기 부착)와 공유하기 위해 NpcInteractionValidation으로 옮겼다. 동작은 기존과 동일.
        if (!NpcInteractionValidation.TryGetInteractionCollider(NetworkManager, rpcParams.Receive.SenderClientId, out SphereCollider interactionCollider) ||
            !NpcInteractionValidation.IsWithinInteractionRange(transform.position, interactionCollider, _rangeTolerance))
        {
            return;
        }

        GetComponent<NpcMovement>()?.HoldExternally();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestResumeAfterCancelRpc(RpcParams rpcParams = default)
    {
        if (!IsSpawned)
        {
            return;
        }

        GetComponent<NpcMovement>()?.ReleaseExternalHold();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestSelectArrestCandidateRpc(RpcParams rpcParams = default)
    {
        if (!IsSpawned)
        {
            return;
        }

        if (!NpcInteractionValidation.TryGetInteractionCollider(NetworkManager, rpcParams.Receive.SenderClientId, out SphereCollider interactionCollider) ||
            !NpcInteractionValidation.IsWithinInteractionRange(transform.position, interactionCollider, _rangeTolerance))
        {
            return;
        }

        // 후보 지정이 성공했을 때만 같은 흐름에서 바로 투표를 시작해서, 후보 미지정 상태로 투표가 시작되는 경쟁 상태를 막는다.
        // 투표 시작패널에서 [네] 클릭후 바로 부결처리가 되는 원인
        if (ArrestVoteManager.Instance != null && ArrestVoteManager.Instance.TrySetArrestCandidate(NetworkObject))
        {
            ArrestVoteManager.Instance.RequestStartVoteServerRpc();
        }
    }
}
