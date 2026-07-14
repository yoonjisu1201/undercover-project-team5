using System.Collections.Generic;
using UnityEngine;

public sealed class MapBlockController : MonoBehaviour
{
    [SerializeField] private MapBlock[] _startingAvailableBlocks;
    [SerializeField] private MapBlockLink[] _blockLinks;

    private readonly List<MapBlock> _availableBlocks = new();

    public IReadOnlyList<MapBlock> AvailableBlocks => _availableBlocks;

    /// <summary>
    /// 시작 Block을 중복 없이 등록하고 모든 Link의 활성 상태를 갱신합니다.
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
    /// 지정한 Block을 해제 목록에 추가하고 Link의 활성 상태를 갱신합니다.
    /// </summary>
    /// <param name="block">새로 해제할 Block입니다.</param>
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
    /// 지정한 Block이 현재 해제되어 있는지 확인합니다.
    /// </summary>
    /// <param name="block">확인할 Block입니다.</param>
    /// <returns>Block이 해제 목록에 있으면 true입니다.</returns>
    public bool IsBlockAvailable(MapBlock block)
    {
        return block != null && _availableBlocks.Contains(block);
    }

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
