using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

// MapBlock의 NavMeshSurface를 단서 생성 구역으로 제공하는 클래스
public sealed class ClueSpawnArea : MonoBehaviour
{
    [Header("MapBlock 설정")]
    [SerializeField] private MapBlockController _blockController;
    [SerializeField] private MapBlock _mapBlock;

    public bool IsConfigured =>
        _blockController != null &&
        _mapBlock != null &&
        _mapBlock.TryGetComponent(out NavMeshSurface surface) &&
        surface.navMeshData != null;

    public bool IsUnlocked =>
        gameObject.activeInHierarchy &&
        IsConfigured &&
        _blockController.IsBlockAvailable(_mapBlock);

    //--- 해금된 MapBlock의 NavMeshSurface에서 랜덤 위치 탐색 ---//
    public bool TryGetRandomNavMeshPoint(
        float sampleDistance,
        float maximumDistance,
        out Vector3 position)
    {
        position = default;

        if (!IsUnlocked || !_mapBlock.TryGetComponent(out NavMeshSurface surface))
        {
            return false;
        }

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        var triangleStarts = new List<int>();
        var triangleAreas = new List<float>();
        float totalArea = 0f;

        //--- 현재 Surface Volume 안에 포함된 NavMesh 삼각형 수집 ---//
        for (int index = 0; index < triangulation.indices.Length; index += 3)
        {
            Vector3 vertexA = triangulation.vertices[triangulation.indices[index]];
            Vector3 vertexB = triangulation.vertices[triangulation.indices[index + 1]];
            Vector3 vertexC = triangulation.vertices[triangulation.indices[index + 2]];
            Vector3 center = (vertexA + vertexB + vertexC) / 3f;

            if (!IsInsideSurfaceVolume(surface, center) ||
                !IsWithinMaximumDistance(surface, center, maximumDistance))
            {
                continue;
            }

            float area = Vector3.Cross(vertexB - vertexA, vertexC - vertexA).magnitude * 0.5f;
            if (area <= Mathf.Epsilon)
            {
                continue;
            }

            triangleStarts.Add(index);
            triangleAreas.Add(area);
            totalArea += area;
        }

        if (triangleStarts.Count == 0)
        {
            return false;
        }

        //--- 삼각형 면적에 비례하여 하나를 선택 ---//
        float randomArea = Random.value * totalArea;
        int selectedTriangle = triangleStarts[^1];
        for (int index = 0; index < triangleStarts.Count; index++)
        {
            randomArea -= triangleAreas[index];
            if (randomArea <= 0f)
            {
                selectedTriangle = triangleStarts[index];
                break;
            }
        }

        Vector3 a = triangulation.vertices[triangulation.indices[selectedTriangle]];
        Vector3 b = triangulation.vertices[triangulation.indices[selectedTriangle + 1]];
        Vector3 c = triangulation.vertices[triangulation.indices[selectedTriangle + 2]];

        // 삼각형 내부에서 균일한 랜덤 좌표를 계산한다.
        float root = Mathf.Sqrt(Random.value);
        float edgeRatio = Random.value;
        Vector3 randomPoint =
            (1f - root) * a +
            root * (1f - edgeRatio) * b +
            root * edgeRatio * c;

        if (!IsWithinMaximumDistance(surface, randomPoint, maximumDistance))
        {
            return false;
        }

        if (!NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas))
        {
            return false;
        }

        position = hit.position;
        return true;
    }

    //--- 월드 좌표가 현재 NavMeshSurface의 로컬 Volume 안인지 검사 ---//
    private static bool IsInsideSurfaceVolume(NavMeshSurface surface, Vector3 worldPosition)
    {
        Vector3 localPosition = surface.transform.InverseTransformPoint(worldPosition) - surface.center;
        Vector3 halfSize = surface.size * 0.5f;

        return Mathf.Abs(localPosition.x) <= halfSize.x &&
               Mathf.Abs(localPosition.y) <= halfSize.y &&
               Mathf.Abs(localPosition.z) <= halfSize.z;
    }

    //--- Surface 중심을 기준으로 최대 스폰 반경 검사 ---//
    private static bool IsWithinMaximumDistance(
        NavMeshSurface surface,
        Vector3 worldPosition,
        float maximumDistance)
    {
        if (maximumDistance <= 0f)
        {
            return true;
        }

        Vector3 surfaceCenter = surface.transform.TransformPoint(surface.center);
        Vector2 centerOnGround = new Vector2(surfaceCenter.x, surfaceCenter.z);
        Vector2 positionOnGround = new Vector2(worldPosition.x, worldPosition.z);
        return (positionOnGround - centerOnGround).sqrMagnitude <= maximumDistance * maximumDistance;
    }
}
