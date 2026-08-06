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

    [Header("=== 씬 시작 시 활성화할 구역 ===")]
    [SerializeField] private RegionId _initialRegionId;

    private readonly List<MapRegion> _availableRegions = new();

    public IReadOnlyList<MapRegion> Regions => _regions;

    // 씬 시작 시 초기 구역을 활성화하고, CCTV 초기화합니다.
    private void Awake() {
        _cctvHub.Initialize();
        SetActiveRegion(_initialRegionId);
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

        if (_cctvHub != null)
        {
            _cctvHub.ActivateCCTVInRegion(regionId);
        }
        
        RefreshSpawnAreas();
        return true;
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
