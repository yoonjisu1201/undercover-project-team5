using UnityEngine;

// BoxCollider 관련 공용 좌표 변환 유틸리티
public static class BoxColliderUtility
{
    // BoxCollider의 center/size는 로컬 좌표라, 월드 경계를 그대로 대입할 수 없어 변환해준다.
    // 회전은 없다고 가정한다(대상 오브젝트는 축 정렬 상태로 배치됨).
    public static void ApplyWorldBounds(BoxCollider collider, Bounds worldBounds)
    {
        Transform colliderTransform = collider.transform;
        Vector3 lossyScale = colliderTransform.lossyScale;

        collider.center = colliderTransform.InverseTransformPoint(worldBounds.center);
        collider.size = new Vector3(
            worldBounds.size.x / Mathf.Max(lossyScale.x, 0.0001f),
            worldBounds.size.y / Mathf.Max(lossyScale.y, 0.0001f),
            worldBounds.size.z / Mathf.Max(lossyScale.z, 0.0001f));
    }
}
