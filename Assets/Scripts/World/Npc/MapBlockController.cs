using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Inspector에서 지정한 시작 MapBlock으로 NPC 이용 가능 목록을 구성하고 조회합니다.
/// </summary>
public sealed class MapBlockController : MonoBehaviour
{
    [SerializeField] private MapBlock[] _startingAvailableBlocks = Array.Empty<MapBlock>();
    //추가----------------------
    [SerializeField] private MapBlockLink[] _blockLinks = Array.Empty<MapBlockLink>();

    private readonly List<MapBlock> _availableBlocks = new();

    /// <summary>
    /// 현재 NPC가 이용할 수 있는 MapBlock 목록입니다.
    /// </summary>
    public IReadOnlyList<MapBlock> AvailableBlocks => _availableBlocks;

    /// <summary>
    /// Scene에 설정된 시작 MapBlock으로 이용 가능 목록을 초기화합니다.
    /// </summary>
    private void Awake()
    {
        //수정----------------------
        _availableBlocks.Clear();

        if (_startingAvailableBlocks != null)
        {
            foreach (MapBlock block in _startingAvailableBlocks)
            {
                if (block != null && !_availableBlocks.Contains(block))
                {
                    _availableBlocks.Add(block);
                }
            }
        }

        if (_availableBlocks.Count == 0)
        {
            Debug.LogError("[MapBlockController] 시작 가능한 MapBlock이 없습니다.", this);
        }

        //추가----------------------
        RefreshBlockLinks();
    }

    /// <summary>
    /// 지정한 MapBlock이 현재 이용 가능한지 확인합니다.
    /// </summary>
    /// <param name="block">이용 가능 여부를 확인할 MapBlock입니다.</param>
    /// <returns>유효한 MapBlock이 이용 가능 목록에 있으면 true입니다.</returns>
    public bool IsBlockAvailable(MapBlock block)
    {
        return block != null && _availableBlocks.Contains(block);
    }

    //추가----------------------
    /// <summary>
    /// 잠긴 MapBlock을 이용 가능 목록에 한 번만 추가하고 연결 지점 상태를 갱신합니다.
    /// </summary>
    /// <param name="block">해금할 MapBlock입니다.</param>
    /// <returns>새 MapBlock이 추가되었으면 true이고, null 또는 중복 요청이면 false입니다.</returns>
    public bool UnlockBlock(MapBlock block)
    {
        if (block == null || _availableBlocks.Contains(block))
        {
            return false;
        }

        _availableBlocks.Add(block);
        RefreshBlockLinks();
        return true;
    }

    /// <summary>
    /// 모든 MapBlock 연결 지점이 현재 해금 상태를 반영하도록 갱신합니다.
    /// </summary>
    private void RefreshBlockLinks()
    {
        if (_blockLinks == null)
        {
            return;
        }

        foreach (MapBlockLink blockLink in _blockLinks)
        {
            if (blockLink != null)
            {
                blockLink.RefreshAvailability(this);
            }
        }
    }
}
