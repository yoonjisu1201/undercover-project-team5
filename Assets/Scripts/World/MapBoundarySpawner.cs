using System.Collections.Generic;
using UnityEngine;

// 선택된 맵의 경계만 프리팹으로 생성해 씬 파일과 렌더링 비용을 줄입니다.
public sealed class MapBoundarySpawner : MonoBehaviour
{
    private const float SameLineTolerance = 0.5f;

    // 같은 줄로 묶을 수 있는 높이 차이. 2층 구조에서 위층과 아래층을 갈라 놓는 기준이다.
    private const float SameHeightTolerance = 2f;

    private const float PositionKeyScale = 10f;

    [SerializeField] private MapRegionController _regionController;
    [SerializeField] private MapBoundaryLayout[] _layouts;
    [SerializeField] private GameObject _barrierPrefab;
    [SerializeField] private GameObject _fogPrefab;

    private readonly Dictionary<RegionId, MapBoundaryLayout> _layoutByRegion = new();
    private readonly List<GameObject> _spawnedObjects = new();
    private MapRegion _activeRegion;

    private void Awake()
    {
        _layoutByRegion.Clear();
        if (_layouts != null)
        {
            foreach (MapBoundaryLayout layout in _layouts)
            {
                if (layout != null)
                {
                    if (_layoutByRegion.ContainsKey(layout.RegionId))
                    {
                        Debug.LogWarning($"[MapBoundarySpawner] {layout.RegionId} 지역의 경계 배치 데이터가 중복 등록되어 마지막 값으로 덮어씁니다.", this);
                    }

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
        SpawnBarrierPlacements(layout.Barriers, _barrierPrefab, $"BoundaryBarriers_{region.RegionId}");
        SpawnPlacements(layout.Fogs, _fogPrefab, $"BoundaryFogs_{region.RegionId}", scaleParticleShapeOnly: true);
    }

    private void SpawnBarrierPlacements(
        MapBoundaryLayout.Placement[] placements,
        GameObject prefab,
        string rootName)
    {
        if (prefab == null || placements == null || placements.Length == 0)
        {
            Debug.LogError("[MapBoundarySpawner] 바리게이트 프리팹 또는 배치 데이터가 비어 있어 경계를 생성할 수 없습니다.", this);
            return;
        }

        GameObject root = new(rootName);
        root.transform.SetParent(transform, false);
        _spawnedObjects.Add(root);

        List<MapBoundaryLayout.Placement> spawnPlacements = BuildCompleteBarrierPlacements(placements);
        foreach (MapBoundaryLayout.Placement placement in spawnPlacements)
        {
            SpawnPlacement(prefab, placement, root.transform, scaleParticleShapeOnly: false);
        }
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
            SpawnPlacement(prefab, placement, root.transform, scaleParticleShapeOnly);
        }
    }

    private static void SpawnPlacement(
        GameObject prefab,
        MapBoundaryLayout.Placement placement,
        Transform parent,
        bool scaleParticleShapeOnly)
    {
        GameObject instance = Instantiate(prefab, placement.Position, placement.Rotation, parent);
        if (scaleParticleShapeOnly)
        {
            ScaleParticleShapes(instance, placement.Scale);
        }
        else
        {
            instance.transform.localScale = placement.Scale;
        }
    }

    private static List<MapBoundaryLayout.Placement> BuildCompleteBarrierPlacements(MapBoundaryLayout.Placement[] placements)
    {
        List<MapBoundaryLayout.Placement> completePlacements = new();
        HashSet<string> generatedPositionKeys = new();

        GenerateContinuousBarrierLines(placements, completePlacements, generatedPositionKeys, groupByZ: true);
        GenerateContinuousBarrierLines(placements, completePlacements, generatedPositionKeys, groupByZ: false);

        foreach (MapBoundaryLayout.Placement placement in placements)
        {
            AddUniquePlacement(completePlacements, generatedPositionKeys, placement);
        }

        return completePlacements;
    }

    private static void GenerateContinuousBarrierLines(
        MapBoundaryLayout.Placement[] placements,
        List<MapBoundaryLayout.Placement> completePlacements,
        HashSet<string> generatedPositionKeys,
        bool groupByZ)
    {
        List<List<MapBoundaryLayout.Placement>> lines = BuildPlacementLines(placements, groupByZ);
        foreach (List<MapBoundaryLayout.Placement> line in lines)
        {
            if (line.Count < 3)
            {
                continue;
            }

            line.Sort((left, right) => GetSortAxis(left.Position, groupByZ)
                .CompareTo(GetSortAxis(right.Position, groupByZ)));

            float expectedSpacing = GetExpectedSpacing(line, groupByZ);
            if (expectedSpacing <= 0f)
            {
                continue;
            }

            float lineStart = GetSortAxis(line[0].Position, groupByZ);
            float lineEnd = GetSortAxis(line[^1].Position, groupByZ);
            float lineLength = Mathf.Abs(lineEnd - lineStart);
            int placementCount = Mathf.Max(2, Mathf.RoundToInt(lineLength / expectedSpacing) + 1);

            for (int index = 0; index < placementCount; index++)
            {
                float lineT = placementCount == 1 ? 0f : index / (placementCount - 1f);
                float targetAxis = Mathf.Lerp(lineStart, lineEnd, lineT);
                MapBoundaryLayout.Placement generatedPlacement = EvaluateLinePlacement(line, targetAxis, groupByZ);
                AddUniquePlacement(completePlacements, generatedPositionKeys, generatedPlacement);
            }
        }
    }

    private static List<List<MapBoundaryLayout.Placement>> BuildPlacementLines(
        MapBoundaryLayout.Placement[] placements,
        bool groupByZ)
    {
        List<List<MapBoundaryLayout.Placement>> lines = new();
        foreach (MapBoundaryLayout.Placement placement in placements)
        {
            float lineAxis = GetLineAxis(placement.Position, groupByZ);
            List<MapBoundaryLayout.Placement> matchingLine = null;

            foreach (List<MapBoundaryLayout.Placement> line in lines)
            {
                if (Mathf.Abs(GetLineAxis(line[0].Position, groupByZ) - lineAxis) > SameLineTolerance)
                {
                    continue;
                }

                // 높이까지 봐야 한다. 다리 위와 아래를 한 줄로 묶으면 그 사이를 보간해
                // 공중에 뜬 바리게이트가 생기고, 정작 아래층 바리게이트는 밀려난다.
                if (Mathf.Abs(line[0].Position.y - placement.Position.y) > SameHeightTolerance)
                {
                    continue;
                }

                matchingLine = line;
                break;
            }

            if (matchingLine == null)
            {
                matchingLine = new List<MapBoundaryLayout.Placement>();
                lines.Add(matchingLine);
            }

            matchingLine.Add(placement);
        }

        return lines;
    }

    private static float GetExpectedSpacing(List<MapBoundaryLayout.Placement> line, bool groupByZ)
    {
        List<float> spacings = new();
        for (int index = 1; index < line.Count; index++)
        {
            float spacing = Mathf.Abs(
                GetSortAxis(line[index].Position, groupByZ) -
                GetSortAxis(line[index - 1].Position, groupByZ));

            if (spacing > 0.1f)
            {
                spacings.Add(spacing);
            }
        }

        if (spacings.Count == 0)
        {
            return 0f;
        }

        spacings.Sort();
        return spacings[spacings.Count / 2];
    }

    private static MapBoundaryLayout.Placement EvaluateLinePlacement(
        List<MapBoundaryLayout.Placement> line,
        float targetAxis,
        bool groupByZ)
    {
        for (int index = 1; index < line.Count; index++)
        {
            MapBoundaryLayout.Placement previous = line[index - 1];
            MapBoundaryLayout.Placement next = line[index];
            float previousAxis = GetSortAxis(previous.Position, groupByZ);
            float nextAxis = GetSortAxis(next.Position, groupByZ);

            if (targetAxis > nextAxis)
            {
                continue;
            }

            float t = Mathf.InverseLerp(previousAxis, nextAxis, targetAxis);
            return new MapBoundaryLayout.Placement
            {
                Position = Vector3.Lerp(previous.Position, next.Position, t),
                Rotation = Quaternion.Slerp(previous.Rotation, next.Rotation, t),
                Scale = Vector3.Lerp(previous.Scale, next.Scale, t)
            };
        }

        return line[^1];
    }

    private static void AddUniquePlacement(
        List<MapBoundaryLayout.Placement> placements,
        HashSet<string> positionKeys,
        MapBoundaryLayout.Placement placement)
    {
        if (positionKeys.Add(GetPositionKey(placement.Position)))
        {
            placements.Add(placement);
        }
    }

    // 높이를 빼면 2층 구조(E 지역의 다리 위/아래)에서 같은 XZ에 놓인 아래층 바리게이트가
    // 중복으로 판정돼 통째로 사라진다. 실제로 E 지역에서만 21개가 이렇게 버려지고 있었다.
    private static string GetPositionKey(Vector3 position)
    {
        int x = Mathf.RoundToInt(position.x * PositionKeyScale);
        int y = Mathf.RoundToInt(position.y * PositionKeyScale);
        int z = Mathf.RoundToInt(position.z * PositionKeyScale);
        return $"{x}:{y}:{z}";
    }

    private static float GetLineAxis(Vector3 position, bool groupByZ) => groupByZ ? position.z : position.x;

    private static float GetSortAxis(Vector3 position, bool groupByZ) => groupByZ ? position.x : position.z;

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
