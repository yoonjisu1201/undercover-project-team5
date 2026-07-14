using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// 하나의 NavMeshSurface와 해당 구역에서 사용할 Checkpoint 목록을 소유합니다.
/// </summary>
[RequireComponent(typeof(NavMeshSurface))]
public sealed class MapBlock : MonoBehaviour
{
    [SerializeField] private NpcCheckpoint[] _checkpoints;

    /// <summary>
    /// 이 Block에서 목적지와 스폰 후보로 사용할 Checkpoint 목록입니다.
    /// </summary>
    public IReadOnlyList<NpcCheckpoint> Checkpoints => _checkpoints;
}
