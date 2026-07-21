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

        float rayOriginY = Mathf.Max(
            navMeshPosition.y + _groundRaycastHeight,
            _spawnBounds.bounds.max.y + 0.1f);
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
