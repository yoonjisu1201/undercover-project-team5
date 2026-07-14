using UnityEngine;

/// <summary>
/// 예약된 Checkpoint와 그 안의 실제 이동 위치를 묶은 목적지 값입니다.
/// </summary>
public sealed class NpcDestination
{
    public Vector3 Position { get; }
    public NpcCheckpoint Checkpoint { get; }

    /// <summary>
    /// 이동 위치와 해당 위치의 예약 소유 Checkpoint를 함께 저장합니다.
    /// </summary>
    public NpcDestination(Vector3 position, NpcCheckpoint checkpoint)
    {
        Position = position;
        Checkpoint = checkpoint;
    }
}
