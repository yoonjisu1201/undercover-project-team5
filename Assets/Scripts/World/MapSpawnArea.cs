using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Box Collider 안의 통합 NavMesh 영역을 공용 스폰 구역으로 제공하는 클래스
[RequireComponent(typeof(BoxCollider))]
public sealed class MapSpawnArea : MonoBehaviour
{
    [Header("스폰 구역")]
    [SerializeField] private BoxCollider _spawnBounds;

    [Header("지면 높이 보정")]
    [Tooltip("켜면 레이 시작 높이를 Box 꼭대기 이상으로 강제한다. 다리·고가도로처럼 같은 XZ에 층이 겹치는 야외 지형에 필요한 설정이라 기본값은 켜져 있다. 천장이 낮은 실내 공간(지하 등)에서 켜두면 레이가 항상 천장에 먼저 맞아 검증이 무조건 실패하니 꺼야 한다.")]
    [SerializeField] private bool _clampRayOriginToBoundsTop = true;
    [SerializeField] private LayerMask _groundLayer;
    [SerializeField, Min(0f)] private float _groundRaycastHeight = 20f;
    [SerializeField, Min(0f)] private float _groundRaycastDistance = 50f;
    [SerializeField, Min(0f)] private float _maxGroundHeightDifference = 0.5f;

    private readonly List<int> _triangleStarts = new();
    private readonly List<float> _triangleAreas = new();
    private NavMeshTriangulation _triangulation;

    public bool IsConfigured
    {
        get
        {
            EnsureSpawnBounds();
            return _spawnBounds != null;
        }
    }
    public bool IsAvailable => isActiveAndEnabled && IsConfigured;
    public float NavigableArea { get; private set; }

    private void Reset()
    {
        _spawnBounds = GetComponent<BoxCollider>();
        _spawnBounds.isTrigger = true;
    }

    private void OnValidate()
    {
        EnsureSpawnBounds();
    }

    // 스폰 범위 Box를 월드 좌표 기준 경계에 맞춰 다시 설정한다.
    // 절차적으로 생성되어 매번 크기가 달라지는 맵(지하 등)의 스폰 영역을 실제 생성 결과에 맞출 때 쓴다.
    public void SetBounds(Bounds worldBounds)
    {
        EnsureSpawnBounds();
        BoxColliderUtility.ApplyWorldBounds(_spawnBounds, worldBounds);
    }

    // 통합 NavMesh에서 현재 Box Collider 안에 포함된 삼각형을 수집한다.
    public void RefreshNavMeshTriangles(NavMeshTriangulation triangulation)
    {
        _triangulation = triangulation;
        _triangleStarts.Clear();
        _triangleAreas.Clear();
        NavigableArea = 0f;

        if (!IsAvailable)
        {
            return;
        }

        // 삼각형의 중심점이 Box Collider 안에 있는지 확인하고, 포함되면 면적을 계산해 수집한다.
        for (int index = 0; index < triangulation.indices.Length; index += 3)
        {
            Vector3 vertexA = triangulation.vertices[triangulation.indices[index]];
            Vector3 vertexB = triangulation.vertices[triangulation.indices[index + 1]];
            Vector3 vertexC = triangulation.vertices[triangulation.indices[index + 2]];
            Vector3 center = (vertexA + vertexB + vertexC) / 3f;

            if (!Contains(center) || !IsNearGroundSurface(center))
            {
                continue;
            }

            float area = Vector3.Cross(vertexB - vertexA, vertexC - vertexA).magnitude * 0.5f;
            if (area <= Mathf.Epsilon)
            {
                continue;
            }

            _triangleStarts.Add(index);
            _triangleAreas.Add(area);
            NavigableArea += area;
        }
    }

    // 수집된 NavMesh 삼각형의 면적에 비례해 구역 안의 임의 지점을 선택한다.
    public bool TryGetRandomNavMeshPoint(float sampleDistance, out Vector3 position)
    {
        position = default;

        if (!IsAvailable || _triangleStarts.Count == 0)
        {
            return false;
        }

        int selectedTriangle = SelectTriangleByArea();
        Vector3 vertexA = _triangulation.vertices[_triangulation.indices[selectedTriangle]];
        Vector3 vertexB = _triangulation.vertices[_triangulation.indices[selectedTriangle + 1]];
        Vector3 vertexC = _triangulation.vertices[_triangulation.indices[selectedTriangle + 2]];

        float root = Mathf.Sqrt(Random.value);
        float edgeRatio = Random.value;
        Vector3 randomPoint =
            (1f - root) * vertexA +
            root * (1f - edgeRatio) * vertexB +
            root * edgeRatio * vertexC;

        if (!Contains(randomPoint) ||
            !NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas) ||
            !Contains(hit.position) ||
            !IsNearGroundSurface(hit.position))
        {
            return false;
        }

        position = hit.position;
        return true;
    }

    // NavMesh 위치의 XZ를 유지하면서 실제 Ground Collider의 표면 높이를 구한다.
    public bool TryGetGroundPoint(Vector3 navMeshPosition, out Vector3 groundPosition)
    {
        int groundMask = _groundLayer.value != 0
            ? _groundLayer.value
            : LayerMask.GetMask("Ground");

        float rayOriginY = navMeshPosition.y + _groundRaycastHeight;
        if (_clampRayOriginToBoundsTop)
        {
            // 다리 밑 등 같은 XZ에 층이 겹치는 야외 지형에서, 위쪽 구조물을 뚫고 지나가
            // 그 아래 지면까지 정확히 재려면 레이가 Box 꼭대기보다 위에서 시작해야 한다.
            rayOriginY = Mathf.Max(rayOriginY, _spawnBounds.bounds.max.y + 0.1f);
        }

        Vector3 rayOrigin = new Vector3(navMeshPosition.x, rayOriginY, navMeshPosition.z);
        if (groundMask == 0 ||
            !Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit groundHit,
                _groundRaycastDistance,
                groundMask,
                QueryTriggerInteraction.Ignore) ||
            !Contains(groundHit.point))
        {
            groundPosition = default;
            return false;
        }

        groundPosition = groundHit.point;
        return true;
    }

    // 실제 Ground 표면과 NavMesh의 높이 차이가 스폰 가능한 범위인지 확인한다.
    public bool IsNearGroundSurface(Vector3 navMeshPosition)
    {
        return TryGetGroundPoint(navMeshPosition, out Vector3 groundPosition) &&
               Mathf.Abs(groundPosition.y - navMeshPosition.y) <= _maxGroundHeightDifference;
    }

    // 수집된 삼각형 중 면적 비율로 하나를 선택한다.
    // 면적이 큰 삼각형일수록 선택될 확률이 높다.
    private int SelectTriangleByArea()
    {
        float randomArea = Random.value * NavigableArea;

        for (int index = 0; index < _triangleStarts.Count; index++)
        {
            randomArea -= _triangleAreas[index];
            if (randomArea <= 0f)
            {
                return _triangleStarts[index];
            }
        }

        return _triangleStarts[^1];
    }

    // 월드 좌표가 Box Collider 안에 있는지 확인한다.
    private bool Contains(Vector3 worldPosition)
    {
        Vector3 localPosition = _spawnBounds.transform.InverseTransformPoint(worldPosition) - _spawnBounds.center;
        Vector3 halfSize = _spawnBounds.size * 0.5f;

        return Mathf.Abs(localPosition.x) <= halfSize.x &&
               Mathf.Abs(localPosition.y) <= halfSize.y &&
               Mathf.Abs(localPosition.z) <= halfSize.z;
    }

    // Box Collider가 설정되어 있는지 확인하고, 없으면 GetComponent로 가져온다.
    private void EnsureSpawnBounds()
    {
        if (_spawnBounds == null)
        {
            _spawnBounds = GetComponent<BoxCollider>();
        }
    }
}
