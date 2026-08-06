using UnityEngine;

// P2가 안테나를 들고 들어와야 하는 지점이다. 목표 방향 쪽에 배치되므로 P1이 읽어낸 각도가 곧 이 존의 방향이 된다.
// 진입·이탈 판정은 서버에서만 하고, 결과는 공유 상태(FrequencySyncState)를 통해 세 화면에 전파된다.
[RequireComponent(typeof(SphereCollider))]
public sealed class FrequencyAntennaZone : MonoBehaviour
{
    // 안테나 아이템 ID는 공유 상태(FrequencySyncState)가 들고 있는 값을 그대로 쓴다. 두 곳에 따로 적어 어긋나는 걸 막는다.
    [SerializeField] private FrequencySyncState _syncState;

    private void Awake()
    {
        SphereCollider zoneCollider = GetComponent<SphereCollider>();
        zoneCollider.isTrigger = true;

        _syncState ??= GetComponentInParent<FrequencySyncState>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (HoldsAntenna(other))
        {
            _syncState?.SetAntennaPlacedOnServer(true);
        }
    }

    // 안테나를 들고 존을 벗어나면 진행이 멈춘다. 맞춰둔 주파수 자체는 남으므로 다시 들어오면 이어서 진행된다.
    private void OnTriggerExit(Collider other)
    {
        if (HoldsAntenna(other))
        {
            _syncState?.SetAntennaPlacedOnServer(false);
        }
    }

    // 인벤토리 어느 칸에든 안테나가 있으면 들고 있는 것으로 본다.
    private bool HoldsAntenna(Collider other)
    {
        if (_syncState == null || !_syncState.IsServer)
        {
            return false;
        }

        // 콜라이더가 플레이어 루트가 아닌 자식에 붙어 있을 수 있으므로 부모까지 올라가며 찾는다.
        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null)
        {
            return false;
        }

        foreach (InventorySlot slot in inventory.Slots)
        {
            if (slot != null && slot.ItemId == _syncState.AntennaItemId)
            {
                return true;
            }
        }

        return false;
    }
}
