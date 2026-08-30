using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;

// #803: 벤치의 좌석 하나가 착석·기상 가능 여부, 실행 동작, 안내 문구를 함께 제공한다.
// #803: 플레이어 이동과 네트워크 단계 전환은 PlayerMoveSample에 맡기고 좌석은 상호작용 대상 책임만 가진다.
[RequireComponent(typeof(SphereCollider))]
public sealed class WaitingRoomBenchSeatInteractable : InteractableBase
{
    // #803: NetworkVariable에 저장된 좌석 ID를 현재 씬의 실제 좌석 컴포넌트로 찾기 위한 등록 목록이다.
    private static readonly List<WaitingRoomBenchSeatInteractable> _seats = new();

    // #803: 대기방 안에서 좌석을 식별하고 여러 플레이어의 동일 좌석 점유를 막는 고유 ID다.
    [SerializeField, Min(0)] private int _seatId;

    // #803: 동일한 좌석이 착석과 기상을 모두 처리하므로 현재 이용자의 다음 동작 문구를 별도로 보관한다.
    [SerializeField] private LocalizedString _standInteractionText;
    // #803: 로컬 조준 판정과 서버 거리 검증이 동일한 좌석 범위를 사용하도록 캐시한다.
    private SphereCollider _interactionCollider;

    // #803: 좌석 메시의 피벗이 아닌 실제 상호작용 콜라이더 중심을 조준 기준으로 사용한다.
    public override Vector3 InteractionPosition => _interactionCollider.bounds.center;

    protected override void Awake()
    {
        base.Awake();
        _interactionCollider = GetComponent<SphereCollider>();
    }

    // #803: 같은 좌석에서 서 있는 플레이어는 착석을, 현재 이용자는 기상을 수행할 수 있도록 상태별 가능 여부를 판단한다.
    public override bool CanInteract(GameObject interactor)
    {
        if (!interactor.TryGetComponent(out PlayerMoveSample playerMove))
        {
            return false;
        }

        // #803: 현재 이용자는 자신의 점유 때문에 차단되지 않도록 일반 좌석 점유 검사보다 먼저 기상 가능 여부를 확인한다.
        if (playerMove.IsCurrentSeat(_seatId))
        {
            return playerMove.CanStand;
        }

        return playerMove.CanSit && !PlayerMoveSample.IsSeatOccupied(_seatId);
    }

    // #803: 앉기와 기상을 같은 Interactable 진입점으로 처리해 PlayerInteraction이 구체 동작을 직접 선택하지 않게 한다.
    public override void Interact(GameObject interactor)
    {
        if (!interactor.TryGetComponent(out PlayerMoveSample playerMove))
        {
            return;
        }

        if (playerMove.IsCurrentSeat(_seatId))
        {
            playerMove.RequestStand();
            return;
        }

        playerMove.RequestSit(_seatId);
    }

    // #803: 서 있을 때는 InteractableBase의 앉기 문구를, 현재 이용자에게는 기상 문구를 제공한다.
    public override string GetInteractionText(GameObject interactor)
    {
        if (!interactor.TryGetComponent(out PlayerMoveSample playerMove) || !playerMove.IsCurrentSeat(_seatId))
        {
            return base.GetInteractionText(interactor);
        }

        // #803: 앉기·기상 전환 중에는 아직 받을 수 있는 입력이 없으므로 문구를 숨긴다.
        return playerMove.CanStand ? _standInteractionText.GetLocalizedString() : null;
    }

    // #803: 전환 중 빈 문구 옆에 [E]만 남지 않도록 기상 가능할 때만 키 힌트를 보인다.
    public override bool ShowInteractionKeyHint(GameObject interactor)
    {
        if (interactor.TryGetComponent(out PlayerMoveSample playerMove) && playerMove.IsCurrentSeat(_seatId))
        {
            return playerMove.CanStand;
        }

        return base.ShowInteractionKeyHint(interactor);
    }

    // #803: 서버가 클라이언트의 착석 요청을 승인하기 전에 플레이어 상호작용 범위와 좌석 범위를 다시 비교한다.
    public bool IsWithinInteractionRange(SphereCollider interactionCollider)
    {
        return IsOverlappingInteractionCollider(interactionCollider);
    }

    // #803: 서버가 착석 위치와 방향을 확정할 때 사용할 좌석 Transform 포즈를 제공한다.
    public void GetSeatPose(out Vector3 position, out Quaternion rotation)
    {
        position = transform.position;
        rotation = transform.rotation;
    }

    // #803: 서버 RPC 검증과 착석 중 입력이 동기화된 ID로 동일한 좌석 인스턴스를 찾도록 한다.
    public static bool TryGetSeat(int seatId, out WaitingRoomBenchSeatInteractable seat)
    {
        foreach (WaitingRoomBenchSeatInteractable candidate in _seats)
        {
            if (candidate._seatId == seatId)
            {
                seat = candidate;
                return true;
            }
        }

        seat = null;
        return false;
    }

    // #803: 활성 좌석만 동기화된 ID 조회 대상으로 등록한다.
    private void OnEnable()
    {
        _seats.Add(this);
    }

    // #803: 비활성 좌석을 ID 조회에서 제외하고 서버에 남은 플레이어 점유도 해제한다.
    private void OnDisable()
    {
        _seats.Remove(this);
        PlayerMoveSample.ForceReleaseSeatOnServer(_seatId);
    }
}
