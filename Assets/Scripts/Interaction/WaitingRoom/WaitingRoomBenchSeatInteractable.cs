using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
public sealed class WaitingRoomBenchSeatInteractable : InteractableBase
{
    private static readonly List<WaitingRoomBenchSeatInteractable> Seats = new();

    [SerializeField, Min(0)] private int _seatId;
    private SphereCollider _interactionCollider;

    public override Vector3 InteractionPosition => _interactionCollider.bounds.center;

    protected override void Awake()
    {
        base.Awake();
        _interactionCollider = GetComponent<SphereCollider>();
    }

    public override bool CanInteract(GameObject interactor)
    {
        return interactor.TryGetComponent(out PlayerMoveSample playerMove)
            && playerMove.CanSit
            && !PlayerMoveSample.IsSeatOccupied(_seatId);
    }

    public override void Interact(GameObject interactor)
    {
        if (interactor.TryGetComponent(out PlayerMoveSample playerMove))
        {
            playerMove.RequestSit(_seatId);
        }
    }

    public bool IsWithinInteractionRange(SphereCollider interactionCollider)
    {
        return IsOverlappingInteractionCollider(interactionCollider);
    }

    public void GetSeatPose(out Vector3 position, out Quaternion rotation)
    {
        position = transform.position;
        rotation = transform.rotation;
    }

    public static bool TryGetSeat(int seatId, out WaitingRoomBenchSeatInteractable seat)
    {
        foreach (WaitingRoomBenchSeatInteractable candidate in Seats)
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

    private void OnEnable()
    {
        Seats.Add(this);
    }

    private void OnDisable()
    {
        Seats.Remove(this);
        PlayerMoveSample.ForceReleaseSeatOnServer(_seatId);
    }
}
