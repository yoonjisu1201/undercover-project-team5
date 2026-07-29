using Unity.Netcode;
using UnityEngine;

// NPC 상호작용 RPC들이 공통으로 쓰는 "요청자 재검증" 로직을 모아둔다.
public static class NpcInteractionValidation
{
    // 클라이언트가 주장하는 대로가 아니라, 서버가 직접 보유한 SphereCollider로 상호작용 반경을 확인한다.
    public static bool TryGetInteractionCollider(NetworkManager networkManager, ulong senderClientId, out SphereCollider interactionCollider)
    {
        interactionCollider = null;

        if (!networkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient senderClient))
        {
            Debug.LogWarning($"[NpcInteractionValidation] clientId {senderClientId}의 연결 정보를 찾을 수 없습니다.");
            return false;
        }

        NetworkObject playerObject = senderClient.PlayerObject;
        if (playerObject == null)
        {
            Debug.LogWarning($"[NpcInteractionValidation] clientId {senderClientId}의 PlayerObject가 아직 스폰되지 않았습니다.");
            return false;
        }

        interactionCollider = playerObject.GetComponent<SphereCollider>();
        if (interactionCollider == null)
        {
            Debug.LogWarning($"[NpcInteractionValidation] clientId {senderClientId}의 플레이어 오브젝트에 상호작용용 SphereCollider가 없습니다.");
            return false;
        }

        return true;
    }

    // 상호작용 요청을 보낸 클라이언트의 인벤토리를 서버에서 직접 찾아온다(클라이언트가 보낸 값은 신뢰하지 않음).
    public static bool TryGetSenderInventory(NetworkManager networkManager, ulong senderClientId, out PlayerInventory inventory)
    {
        inventory = null;

        if (!networkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient senderClient))
        {
            Debug.LogWarning($"[NpcInteractionValidation] clientId {senderClientId}의 연결 정보를 찾을 수 없습니다.");
            return false;
        }

        NetworkObject playerObject = senderClient.PlayerObject;
        if (playerObject == null)
        {
            Debug.LogWarning($"[NpcInteractionValidation] clientId {senderClientId}의 PlayerObject가 아직 스폰되지 않았습니다.");
            return false;
        }

        inventory = playerObject.GetComponent<PlayerInventory>();
        if (inventory == null)
        {
            Debug.LogWarning($"[NpcInteractionValidation] clientId {senderClientId}의 플레이어 오브젝트에 PlayerInventory가 없습니다.");
            return false;
        }

        return true;
    }

    // 정확한 콜라이더 겹침 대신 거리 + 여유값으로 판정해, 네트워크 지연으로 인한 근소한 위치 차이를 흡수한다.
    public static bool IsWithinInteractionRange(Vector3 targetPosition, SphereCollider interactionCollider, float rangeTolerance)
    {
        float maxDistance = interactionCollider.radius + rangeTolerance;
        float distance = Vector3.Distance(targetPosition, interactionCollider.transform.position);
        return distance <= maxDistance;
    }
}
