using UnityEngine;

public class SpawnPointHub : MonoBehaviour
{
    [Header("Site Spawn Settings")]
    [SerializeField] private MapRegionController _regionController;
    [SerializeField, Min(0f)] private float _siteSpawnHeightOffset = 0.2f;

    [Header("HQ Spawn Settings")]
    [SerializeField] private Transform _hqSpawnPoint;

    public Transform HQSpawnPoint => _hqSpawnPoint;

    // 현재 활성화된 현장 구역의 실제 지면 위에서 무작위 스폰 위치를 구합니다.
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

}
