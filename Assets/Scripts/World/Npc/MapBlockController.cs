using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 해금된 Block 목록을 관리하고 양쪽 Block이 열린 Link만 활성화합니다.
/// </summary>
public sealed class MapBlockController : MonoBehaviour
{
    [SerializeField] private MapBlock[] _startingAvailableBlocks;
    [SerializeField] private MapBlockLink[] _blockLinks;

    private readonly List<MapBlock> _availableBlocks = new();

    /// <summary>
    /// 현재 목적지와 경로에 사용할 수 있는 해금 Block 목록입니다.
    /// </summary>
    public IReadOnlyList<MapBlock> AvailableBlocks => _availableBlocks;

    /// <summary>
    /// 시작 Block을 중복 없이 등록하고 모든 Link 상태를 동기화합니다.
    /// </summary>
    private void Awake()
    {
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

        ApplyBlockLinks();
    }

    /// <summary>
    /// 새 Block 해금을 한 번 반영하고 모든 Link를 재평가합니다.
    /// </summary>
    public void UnlockBlock(MapBlock block)
    {
        if (block == null || _availableBlocks.Contains(block))
        {
            return;
        }

        _availableBlocks.Add(block);
        ApplyBlockLinks();
    }

    /// <summary>
    /// Block이 현재 해금 목록에 있는지 확인합니다.
    /// </summary>
    public bool IsBlockAvailable(MapBlock block)
    {
        return block != null && _availableBlocks.Contains(block);
    }

    /// <summary>
    /// 현재 해금 상태를 등록된 모든 Block Link에 전달합니다.
    /// </summary>
    private void ApplyBlockLinks()
    {
        if (_blockLinks == null)
        {
            return;
        }

        foreach (MapBlockLink blockLink in _blockLinks)
        {
            if (blockLink != null)
            {
                blockLink.ApplyAvailability(this);
            }
        }
    }
}
