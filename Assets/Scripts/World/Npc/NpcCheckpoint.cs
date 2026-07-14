using UnityEngine;

public sealed class NpcCheckpoint : MonoBehaviour
{
    [SerializeField, Min(0f)] private float _radius = 3f;
    [SerializeField, Min(1)] private int _capacity = 10;

    private int _reservationCount;

    /// <summary>
    /// Checkpoint가 추가 NPC를 수용할 수 있는지 나타냅니다.
    /// </summary>
    public bool HasAvailableSlot
    {
        get
        {
            return _reservationCount < _capacity;
        }
    }

    /// <summary>
    /// 수용량이 남아 있으면 예약 수를 한 번 증가시킵니다.
    /// </summary>
    /// <returns>예약에 성공하면 true입니다.</returns>
    public bool TryReserve()
    {
        if (!HasAvailableSlot)
        {
            return false;
        }

        _reservationCount++;
        return true;
    }

    /// <summary>
    /// 현재 예약 수를 음수가 되지 않는 범위에서 한 번 감소시킵니다.
    /// </summary>
    public void ReleaseReservation()
    {
        if (_reservationCount > 0)
        {
            _reservationCount--;
        }
    }

    /// <summary>
    /// Checkpoint 중심에서 설정된 반경 안의 임의 XZ 좌표를 반환합니다.
    /// </summary>
    /// <returns>Checkpoint 반경 안의 월드 좌표입니다.</returns>
    public Vector3 RandomPointInRadius()
    {
        Vector2 offset = Random.insideUnitCircle * Mathf.Max(0f, _radius);
        return transform.position + new Vector3(offset.x, 0f, offset.y);
    }

    /// <summary>
    /// 지정한 월드 좌표가 Checkpoint의 XZ 반경 안에 있는지 확인합니다.
    /// </summary>
    /// <param name="worldPosition">확인할 월드 좌표입니다.</param>
    /// <returns>반경 안에 있으면 true입니다.</returns>
    public bool Contains(Vector3 worldPosition)
    {
        Vector3 offset = worldPosition - transform.position;
        float radius = Mathf.Max(0f, _radius);

        return offset.x * offset.x + offset.z * offset.z <= radius * radius;
    }
}
