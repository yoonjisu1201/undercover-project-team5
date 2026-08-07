using UnityEngine;

// 안테나를 설치해야 하는 지점이다. 좌표는 서버가 정하고 이 오브젝트가 그 자리로 옮겨 다닌다.
// 안테나를 들고 들어오면 그 자리에 설치되고 아이템은 소모된다. 판정은 서버에서만 한다.
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

    private void OnTriggerEnter(Collider other) => TryInstall(other);

    // 겹친 채로 좌표가 옮겨 오면 진입 이벤트가 생기지 않으므로, 머무는 동안에도 계속 확인한다.
    private void OnTriggerStay(Collider other) => TryInstall(other);

    private void TryInstall(Collider other)
    {
        if (_syncState == null || !_syncState.IsServer || _syncState.AntennaPlaced)
        {
            return;
        }

        // 콜라이더가 플레이어 루트가 아닌 자식에 붙어 있을 수 있으므로 부모까지 올라가며 찾는다.
        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null || !HasSelectedAntenna(inventory))
        {
            return;
        }

        _syncState.TryInstallAntennaOnServer(inventory, inventory.SelectedIndex, transform.position);
    }

    private bool HasSelectedAntenna(PlayerInventory inventory)
    {
        int selectedIndex = inventory.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= inventory.Slots.Length)
        {
            return false;
        }

        InventorySlot selectedSlot = inventory.Slots[selectedIndex];
        return selectedSlot != null && selectedSlot.ItemId == _syncState.AntennaItemId;
    }
}
