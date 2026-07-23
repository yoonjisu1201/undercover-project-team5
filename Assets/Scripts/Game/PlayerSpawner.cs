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
    
    // 클라이언트들을 적당한 자리에 배치하기
    // 예전에는 서버(호스트) 플레이어를 항상 본부로 고정 배치했으나,
    // 대기실에서 선택한 역할(Player.PlayerRole)을 기준으로 배치하도록 바뀌었다.
    public void SpawnClients() {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;
            // PlayerObject가 아직 스폰 전이거나 필요한 컴포넌트가 없으면 스폰 배치를 건너뛴다(NRE 방지).
            if (playerObject == null ||
                !playerObject.TryGetComponent(out PlayerMoveSample move) ||
                !playerObject.TryGetComponent(out Player player))
            {
                continue;
            }

            if (player.PlayerRole == Role.Headquarter) {
                if (_hqSpawnPoint != null) {
                    move.TeleportToPosition(_hqSpawnPoint.position, _hqSpawnPoint.rotation);
                }
                else {
                    Debug.LogError("본부 HQSpawnPoint가 설정되지 않았습니다.");
                }

                continue;
            }

            if (TryGetSiteSpawnPose(out Vector3 position, out Quaternion rotation)) {
                move.TeleportToPosition(position, rotation);
            }
            else {
                Debug.LogError("활성화된 현장 스폰 구역에서 플레이어 스폰 위치를 찾지 못했습니다.");
            }
        }
    }
}
