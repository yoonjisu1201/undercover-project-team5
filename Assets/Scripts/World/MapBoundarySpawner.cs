using System.Collections.Generic;
using UnityEngine;

// 선택된 맵의 경계만 프리팹으로 생성해 씬 파일과 렌더링 비용을 줄입니다.
public sealed class MapBoundarySpawner : MonoBehaviour
{
    [SerializeField] private MapRegionController _regionController;
    [SerializeField] private MapBoundaryLayout[] _layouts;
    [SerializeField] private GameObject _barrierPrefab;
    [SerializeField] private GameObject _fogPrefab;

    private readonly Dictionary<string, MapBoundaryLayout> _layoutByRegion = new();
    private readonly List<GameObject> _spawnedObjects = new();
    private MapRegion _activeRegion;

    private void Awake()
    {
        _layoutByRegion.Clear();
        if (_layouts != null)
        {
            foreach (MapBoundaryLayout layout in _layouts)
            {
                if (layout != null && !string.IsNullOrWhiteSpace(layout.RegionId))
                {
                    _layoutByRegion[layout.RegionId] = layout;
                }
            }
        }
    }

    private void Start()
    {
        if (_regionController == null)
        {
            _regionController = FindFirstObjectByType<MapRegionController>();
        }

        if (_regionController == null || _regionController.Regions == null)
        {
            Debug.LogError("[MapBoundarySpawner] MapRegionController를 찾을 수 없습니다.", this);
            return;
        }

        foreach (MapRegion region in _regionController.Regions)
        {
            if (region == null)
            {
                continue;
            }

            region.UnlockStateChanged += HandleUnlockStateChanged;
            if (region.IsUnlocked)
            {
                ActivateRegion(region);
            }
        }
    }

    private void OnDestroy()
    {
        if (_regionController == null || _regionController.Regions == null)
        {
            return;
        }

        foreach (MapRegion region in _regionController.Regions)
        {
            if (region != null)
            {
                region.UnlockStateChanged -= HandleUnlockStateChanged;
            }
        }
    }

    private void HandleUnlockStateChanged(MapRegion region, bool unlocked)
    {
        if (unlocked)
        {
            ActivateRegion(region);
        }
        else if (_activeRegion == region)
        {
            ClearSpawnedObjects();
            _activeRegion = null;
        }
    }

    private void ActivateRegion(MapRegion region)
    {
        if (!_layoutByRegion.TryGetValue(region.RegionId, out MapBoundaryLayout layout))
        {
            Debug.LogError($"[MapBoundarySpawner] {region.RegionId} 지역의 경계 배치 데이터가 없습니다.", this);
            return;
        }

        ClearSpawnedObjects();
        _activeRegion = region;
        SpawnPlacements(layout.Barriers, _barrierPrefab, $"BoundaryBarriers_{region.RegionId}", scaleParticleShapeOnly: false);
        SpawnPlacements(layout.Fogs, _fogPrefab, $"BoundaryFogs_{region.RegionId}", scaleParticleShapeOnly: true);
    }

    private void SpawnPlacements(
        MapBoundaryLayout.Placement[] placements,
        GameObject prefab,
        string rootName,
        bool scaleParticleShapeOnly)
    {
        if (prefab == null || placements == null || placements.Length == 0)
        {
            return;
        }

        GameObject root = new(rootName);
        root.transform.SetParent(transform, false);
        _spawnedObjects.Add(root);

        foreach (MapBoundaryLayout.Placement placement in placements)
        {
            GameObject instance = Instantiate(prefab, placement.Position, placement.Rotation, root.transform);
            if (scaleParticleShapeOnly)
            {
                ScaleParticleShapes(instance, placement.Scale);
            }
            else
            {
                instance.transform.localScale = placement.Scale;
            }
        }
    }

    // 파티클 이미지 자체는 키우지 않고 방출 영역만 경계면 길이에 맞춥니다.
    private static void ScaleParticleShapes(GameObject instance, Vector3 scale)
    {
        instance.transform.localScale = Vector3.one;

        foreach (ParticleSystem particleSystem in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.ShapeModule shape = particleSystem.shape;
            shape.scale = Vector3.Scale(shape.scale, scale);
        }
    }

    private void ClearSpawnedObjects()
    {
        foreach (GameObject spawnedObject in _spawnedObjects)
        {
            if (spawnedObject != null)
            {
                spawnedObject.SetActive(false);
                Destroy(spawnedObject);
            }
        }

        _spawnedObjects.Clear();
    }
}
