using Unity.Netcode;
using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    [Header("Site Spawn Settings")]
    [SerializeField] private MapRegionController _regionController;
    [SerializeField, Min(0f)] private float _siteSpawnHeightOffset = 0.2f;

    [Header("HQ Spawn Settings")]
    [SerializeField] private Transform _hqSpawnPoint;

    // 일반 스폰과 긴급 탈출에서 함께 사용할 현장 지면 위치를 구합니다.
    public bool TryGetSiteSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        if (_regionController != null &&
            _regionController.RefreshSpawnAreas() &&
            _regionController.TryGetRandomSpawnPoint(out MapRegion region, out position) &&
            region.SpawnArea.TryGetGroundPoint(position, out Vector3 groundPosition))
        {
            position = groundPosition + Vector3.up * _siteSpawnHeightOffset;
            rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            return true;
        }

        position = default;
        rotation = Quaternion.identity;
        return false;
    }
    
    // 요청한 클라이언트의 역할을 보고, 적절한 위치에 배치한다.
    public void SpawnPlayer(NetworkObject playerObject) {
        // PlayerObject가 아직 스폰 전이거나 필요한 컴포넌트가 없으면 스폰 배치를 건너뛴다(NRE 방지).
        if (playerObject == null ||
            !playerObject.TryGetComponent(out PlayerMoveSample move) ||
            !playerObject.TryGetComponent(out Player player))
        {
            Debug.LogError($"[PlayerSpawner] 아직 캐릭터가 스폰되지 않았습니다.");
            return;
        }

        if (player.PlayerRole == Role.Headquarter) {
            if (_hqSpawnPoint == null) {
                Debug.LogError("[PlayerSpawner] 본부 HQSpawnPoint가 설정되지 않았습니다.");
                return;
            }
            move.TeleportToPosition(_hqSpawnPoint.position, _hqSpawnPoint.rotation);
            return;
        }

        if (TryGetSiteSpawnPose(out Vector3 position, out Quaternion rotation)) {
            move.TeleportToPosition(position, rotation);
        }
        else {
            Debug.LogError("[PlayerSpawner] 활성화된 현장 스폰 구역에서 플레이어 스폰 위치를 찾지 못했습니다.");
        }
    }
}
