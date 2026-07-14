using UnityEngine;

/// <summary>
/// NPC의 목적지 선택과 Checkpoint 예약 수명을 분리하는 Provider 계약입니다.
/// </summary>
public interface INpcDestinationProvider
{
    /// <summary>
    /// 현재 위치와 기존 예약을 기준으로 새 목적지를 예약합니다.
    /// </summary>
    bool TryReserveDestination(
        Vector3 origin,
        NpcCheckpoint currentCheckpoint,
        out NpcDestination destination);

    /// <summary>
    /// 목적지가 보유한 Checkpoint 예약을 반환합니다.
    /// </summary>
    void ReleaseDestination(NpcDestination destination);
}
