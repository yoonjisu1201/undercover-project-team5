using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

// 맵 구역 목록과 현재 해금된 구역의 공용 NavMesh 스폰 영역을 관리합니다.
public sealed class MapRegionController : MonoBehaviour
{
    [Header("Map Regions")]
    [SerializeField] private MapRegion[] _regions;

    [Header("Spawn Point Settings")]
    [SerializeField, Min(1)] private int _maxSpawnAttempts = 50;
    [SerializeField, Min(0.01f)] private float _navMeshSampleDistance = 1f;
    
    [Header("=== 특정 위치가 해금되면 CCTV도 열어주기 위해 CCTVHub등록 ===")]
    [SerializeField] private CCTVHub _cctvHub;

    [Header("=== 활성 구역으로 옮길 시작 지점(StartPoint 캠핑카) ===")]
    [SerializeField] private Transform _startPoint;

    private readonly List<MapRegion> _availableRegions = new();

    public IReadOnlyList<MapRegion> Regions => _regions;

    // 현재 라운드에서 사용 중인 구역. 미니맵처럼 구역별로 표시를 바꿔야 하는 쪽에서 참조한다.
    public MapRegion ActiveRegion { get; private set; }

    // 활성 구역이 바뀐 뒤 호출됩니다.
    public event Action<MapRegion> ActiveRegionChanged;

    // 활성 구역은 RoundManager가 라운드마다 추첨해 SetActiveRegion()으로 지정합니다.
    private void Awake() {
        _cctvHub.Initialize();
    }

    // 한 번에 하나의 맵만 사용하도록 선택한 구역만 활성화합니다.
    public bool SetActiveRegion(RegionId regionId)
    {
        if (_regions == null)
        {
            return false;
        }

        MapRegion selectedRegion = null;
        foreach (MapRegion region in _regions)
        {
            if (region != null &&
                region.RegionId == regionId)
            {
                selectedRegion = region;
                break;
            }
        }

        if (selectedRegion == null)
        {
            return false;
        }

        foreach (MapRegion region in _regions)
        {
            if (region != null)
            {
                region.SetUnlocked(region == selectedRegion);
            }
        }

        ActiveRegion = selectedRegion;
        ActiveRegionChanged?.Invoke(selectedRegion);

        MoveStartPointToRegion(selectedRegion);

        if (_cctvHub != null)
        {
            _cctvHub.ActivateCCTVInRegion(regionId);
        }

        RefreshSpawnAreas();
        return true;
    }

    // 본부 입구, 플레이어 스폰 지점, 카트 스폰 지점이 모두 StartPoint의 자식이라
    // 이 오브젝트만 옮기면 현장 배치가 전부 선택된 구역으로 따라옵니다.
    private void MoveStartPointToRegion(MapRegion region)
    {
        if (_startPoint == null)
        {
            Debug.LogError("[MapRegionController] StartPoint 참조가 비어 있어 시작 지점을 옮길 수 없습니다.", this);
            return;
        }

        if (region.StartPointAnchor == null)
        {
            Debug.LogError($"[MapRegionController] '{region.RegionId}' 구역에 StartPointAnchor가 설정되지 않았습니다.", region);
            return;
        }

        _startPoint.SetPositionAndRotation(
            region.StartPointAnchor.position,
            region.StartPointAnchor.rotation);
    }

    // 월드 위치를 포함하는 해방 지역을 반환합니다.
    public bool TryGetUnlockedRegionAt(Vector3 position, out MapRegion region)
    {
        if (_regions != null)
        {
            foreach (MapRegion candidate in _regions)
            {
                if (candidate != null &&
                    candidate.IsUnlocked &&
                    candidate.SpawnArea != null &&
                    candidate.Contains(position) &&
                    candidate.SpawnArea.IsNearGroundSurface(position))
                {
                    region = candidate;
                    return true;
                }
            }
        }

        region = null;
        return false;
    }

    // 현재 해금된 구역의 NavMesh 삼각형을 갱신합니다.
    public bool RefreshSpawnAreas()
    {
        _availableRegions.Clear();

        if (_regions == null || _regions.Length == 0)
        {
            Debug.LogError("[MapRegionController] 관리할 MapRegion이 없습니다.", this);
            return false;
        }

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();

        foreach (MapRegion region in _regions)
        {
            if (region == null || !region.IsUnlocked || region.SpawnArea == null)
            {
                continue;
            }

            MapSpawnArea spawnArea = region.SpawnArea;
            spawnArea.RefreshNavMeshTriangles(triangulation);

            if (spawnArea.NavigableArea > 0f)
            {
                _availableRegions.Add(region);
            }
        }

        return _availableRegions.Count > 0;
    }

    // 현재 사용 가능한 구역 중 하나를 면적 비율로 선택해 NavMesh 위치를 반환합니다.
    public bool TryGetRandomSpawnPoint(out MapRegion region, out Vector3 position)
    {
        for (int attempt = 0; attempt < _maxSpawnAttempts && _availableRegions.Count > 0; attempt++)
        {
            region = SelectRegionByNavigableArea();
            if (region.SpawnArea.TryGetRandomNavMeshPoint(_navMeshSampleDistance, out position))
            {
                return true;
            }
        }

        region = null;
        position = default;
        return false;
    }

    // 해금된 구역이 여러개일 경우, 각 구역의 NavMesh 면적 비율을 계산하여 NPC 생성 확률을 조정합니다. 
    // 면적이 큰 구역일수록 선택될 확률이 높습니다.
    private MapRegion SelectRegionByNavigableArea()
    {
        float totalArea = 0f;
        foreach (MapRegion region in _availableRegions)
        {
            totalArea += region.SpawnArea.NavigableArea;
        }

        float randomArea = Random.value * totalArea;    // 면적 비율로 구역을 선택합니다.
        foreach (MapRegion region in _availableRegions)
        {
            randomArea -= region.SpawnArea.NavigableArea;
            if (randomArea <= 0f)
            {
                return region;
            }
        }

        return _availableRegions[^1];
    }
}
