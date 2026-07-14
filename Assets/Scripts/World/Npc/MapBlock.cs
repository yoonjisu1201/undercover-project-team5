using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

public sealed class MapBlock : MonoBehaviour
{
    [SerializeField] private NavMeshSurface _surface;
    [SerializeField] private NpcCheckpoint[] _checkpoints;

    public IReadOnlyList<NpcCheckpoint> Checkpoints => _checkpoints;
}
