using UnityEngine;

/// <summary>
/// MapBlock 안에서 NPC 목적지의 반경과 수용 인원 및 예약 수를 관리합니다.
/// </summary>
public sealed class NpcCheckpoint : MonoBehaviour
{
    [SerializeField] private MapBlock _block;
    [SerializeField, Min(0.1f)] private float _radius = 2f;
    [SerializeField, Range(3, 5)] private int _capacity = 3;

    private int _reservedCount;

    /// <summary>
    /// 이 Checkpoint가 속한 논리적 MapBlock입니다.
    /// </summary>
    public MapBlock Block => _block;

    /// <summary>
    /// 목적지 위치를 분산하고 도착 범위를 표현할 반경입니다.
    /// </summary>
    public float Radius => _radius;

    /// <summary>
    /// 이 Checkpoint가 동시에 수용할 수 있는 NPC 수입니다.
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// 현재 이 Checkpoint를 예약한 NPC 수입니다.
    /// </summary>
    public int ReservedCount => _reservedCount;

    /// <summary>
    /// 현재 예약 수가 수용 인원보다 적은지 나타냅니다.
    /// </summary>
    public bool CanReserve => _reservedCount < _capacity;

    /// <summary>
    /// Scene에서 Checkpoint의 반경과 빈자리 여부를 확인할 Gizmo를 표시합니다.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = CanReserve ? Color.cyan : Color.red;
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0f, _radius));
    }

    /// <summary>
    /// 빈자리가 있으면 이 Checkpoint의 예약 수를 하나 늘립니다.
    /// </summary>
    /// <returns>예약에 성공했으면 true입니다.</returns>
    public bool TryReserve()
    {
        //수정----------------------
        if (!CanReserve)
        {
            return false;
        }

        _reservedCount++;
        return true;
    }

    /// <summary>
    /// 예약 수가 0보다 작아지지 않도록 기존 예약 하나를 해제합니다.
    /// </summary>
    public void ReleaseReservation()
    {
        _reservedCount = Mathf.Max(0, _reservedCount - 1);
    }

    /// <summary>
    /// 주어진 위치가 Checkpoint의 XZ 평면 반경 안에 있는지 확인합니다.
    /// </summary>
    /// <param name="worldPosition">포함 여부를 확인할 월드 위치입니다.</param>
    /// <param name="edgeMargin">반경 가장자리에서 안쪽으로 줄일 거리입니다.</param>
    /// <returns>위치가 사용할 수 있는 반경 안에 있으면 true입니다.</returns>
    public bool Contains(Vector3 worldPosition, float edgeMargin = 0f)
    {
        Vector2 offset = new Vector2(
            worldPosition.x - transform.position.x,
            worldPosition.z - transform.position.z);

        float effectiveRadius = Mathf.Max(0f, _radius - Mathf.Max(0f, edgeMargin));
        return offset.sqrMagnitude <= effectiveRadius * effectiveRadius;
    }

    /// <summary>
    /// 정규화된 XZ 오프셋을 Checkpoint 반경 안의 월드 위치로 변환합니다.
    /// </summary>
    /// <param name="normalizedOffset">중심 기준 방향과 비율을 나타내는 2D 오프셋입니다.</param>
    /// <param name="edgeMargin">반경 가장자리에서 안쪽으로 줄일 거리입니다.</param>
    /// <returns>Checkpoint 반경 안으로 제한된 월드 위치입니다.</returns>
    public Vector3 GetPoint(Vector2 normalizedOffset, float edgeMargin = 0f)
    {
        float effectiveRadius = Mathf.Max(0f, _radius - Mathf.Max(0f, edgeMargin));
        Vector2 offset = Vector2.ClampMagnitude(normalizedOffset, 1f) * effectiveRadius;
        return transform.position + new Vector3(offset.x, 0f, offset.y);
    }
}
