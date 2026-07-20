using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// 맵의 논리적 활동 블록과 그 블록에 속한 NPC Checkpoint 목록을 보관합니다.
/// </summary>
[RequireComponent(typeof(NavMeshSurface))]
public sealed class MapBlock : MonoBehaviour
{
    [SerializeField] private string _blockId;
    [SerializeField] private NpcCheckpoint[] _checkpoints = Array.Empty<NpcCheckpoint>();

    /// <summary>
    /// Scene에서 MapBlock을 구분하는 식별자입니다.
    /// </summary>
    public string BlockId => _blockId;

    /// <summary>
    /// 이 MapBlock에 속한 NPC Checkpoint 목록입니다.
    /// </summary>
    public IReadOnlyList<NpcCheckpoint> Checkpoints => _checkpoints;
}
