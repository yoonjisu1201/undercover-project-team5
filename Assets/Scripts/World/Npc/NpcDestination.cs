using UnityEngine;

public sealed class NpcDestination
{
    public Vector3 Position { get; }
    public NpcCheckpoint Checkpoint { get; }

    public NpcDestination(Vector3 position, NpcCheckpoint checkpoint)
    {
        Position = position;
        Checkpoint = checkpoint;
    }
}
