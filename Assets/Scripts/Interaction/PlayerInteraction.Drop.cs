using Unity.Netcode;
using UnityEngine;

// 선택한 아이템을 월드에 드롭한다. 로컬에서 가능 여부만 확인하고 실제 생성/차감은 서버에서 검증한다.
public partial class PlayerInteraction
{
    private void TryDropSelectedItem()
    {
        if (_inventory == null || _itemCatalog == null || _playerCamera == null)
        {
            return;
        }

        // 로컬에서 드롭 가능 여부를 확인하고, 실제 생성과 인벤토리 차감은 서버에 요청한다.
        //--- 선택한 슬롯에 아이템이 있는지 확인 ---//
        if (!_inventory.TryGetSelectedItem(out string itemId)) { Debug.Log("선택한 슬롯에 아이템이 없습니다."); return; }
        if (!_itemCatalog.TryGet(itemId, out ItemData itemData)) { Debug.Log($"ItemCatalog에 '{itemId}'가 없습니다."); return; }
        if (itemData.WorldPrefab == null) { Debug.Log($"'{itemId}'의 WorldPrefab이 없습니다."); return; }

        Transform cameraTransform = _playerCamera.transform;
        Vector3 dropPosition = cameraTransform.position + cameraTransform.forward * 1f;
        Vector3 dropVelocity = cameraTransform.forward * 2f + Vector3.up;

        RequestDropRpc(itemId, _inventory.SelectedIndex, dropPosition, dropVelocity);
        TryCloseVisibleClue();
    }

    [Rpc(SendTo.Server)]
    private void RequestDropRpc(string itemId, int selectedIndex, Vector3 dropPosition, Vector3 dropVelocity)
    {
        // 클라이언트 요청을 신뢰하지 않고 서버에서도 다시 검증한다.
        if (_inventory == null || _itemCatalog == null)
        {
            return;
        }

        if (!_itemCatalog.TryGet(itemId, out ItemData itemData) || itemData.WorldPrefab == null)
        {
            return;
        }

        if (!_inventory.TryRemoveSelectedItemOnServer(itemId, selectedIndex))
        {
            return;
        }

        // 서버가 월드 아이템을 생성하고 네트워크 오브젝트로 스폰한다.
        // 건전지는 원통이 옆으로 눕도록 고정 회전을 사용한다.
        // 나머지 아이템은 프리팹 원본 자세를 유지하면서 플레이어가 바라보는 Y축 방향을 따른다.
        Quaternion playerYaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        Quaternion dropRotation = itemId.StartsWith("Battery", System.StringComparison.OrdinalIgnoreCase)
            ? Quaternion.Euler(90f, 0f, 90f)
            : playerYaw * itemData.WorldPrefab.transform.rotation;
        GameObject droppedObject = Instantiate(itemData.WorldPrefab, dropPosition, dropRotation);

        //--- 드롭한 단서가 기존 단서 번호를 유지하도록 데이터 전달 ---//
        if (droppedObject.TryGetComponent(out PickupItem droppedPickupItem))
        {
            droppedPickupItem.Configure(itemData);
        }

        if (!droppedObject.TryGetComponent(out NetworkObject droppedNetworkObject))
        {
            Debug.LogError($"'{itemData.WorldPrefab.name}' 프리팹에 NetworkObject가 없습니다.");
            Destroy(droppedObject);
            return;
        }

        // 방금 버린 아이템을 바로 다시 줍지 못하도록 잠시 막는다.
        if (droppedObject.TryGetComponent(out PickupItem droppedItem))
        {
            droppedItem.BlockInteraction(_dropInteractionDelay);
        }

        droppedNetworkObject.Spawn();

        // 플레이어가 바라보는 방향으로 초기 속도를 적용한다.
        if (droppedObject.TryGetComponent(out Rigidbody rigidbody))
        {
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.linearVelocity = dropVelocity;
        }
    }
}
