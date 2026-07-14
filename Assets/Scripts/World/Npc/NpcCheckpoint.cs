using UnityEngine;

/// <summary>
/// NPC 스폰·목적지 예약의 수용량과 Checkpoint 반경 샘플을 담당합니다.
/// </summary>
public sealed class NpcCheckpoint : MonoBehaviour
{
    [SerializeField, Min(0f)] private float _radius = 3f;
    [SerializeField, Min(1)] private int _capacity = 10;

    private int _reservationCount;

    /// <summary>
    /// 예약 수가 Checkpoint 수용량 미만인지 확인합니다.
    /// </summary>
    public bool HasAvailableSlot
    {
        get
        {
            return _reservationCount < _capacity;
        }
    }

    /// <summary>
    /// 수용량이 남아 있으면 예약을 한 번 확보합니다.
    /// </summary>
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
    /// 예약 수가 음수가 되지 않도록 한 번 반납합니다.
    /// </summary>
    public void ReleaseReservation()
    {
        if (_reservationCount > 0)
        {
            _reservationCount--;
        }
    }

    /// <summary>
    /// Checkpoint의 XZ 반경 안에서 무작위 월드 좌표를 반환합니다.
    /// </summary>
    public Vector3 RandomPointInRadius()
    {
        Vector2 offset = Random.insideUnitCircle * Mathf.Max(0f, _radius);
        return transform.position + new Vector3(offset.x, 0f, offset.y);
    }

    /// <summary>
    /// 월드 좌표가 Checkpoint의 XZ 반경 안에 있는지 확인합니다.
    /// </summary>
    public bool Contains(Vector3 worldPosition)
    {
        Vector3 offset = worldPosition - transform.position;
        float radius = Mathf.Max(0f, _radius);

        return offset.x * offset.x + offset.z * offset.z <= radius * radius;
    }
}
