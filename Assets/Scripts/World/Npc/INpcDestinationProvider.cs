using UnityEngine;

public interface INpcDestinationProvider
{
    bool TryReserveDestination(
        Vector3 origin,
        NpcCheckpoint currentCheckpoint,
        out NpcDestination destination);

    void ReleaseDestination(NpcDestination destination);
}
