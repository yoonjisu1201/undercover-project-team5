using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// NPC를 고정 용량으로 사전 생성하고 활성·비활성 대여 수명을 관리합니다.
/// </summary>
public sealed class NpcPool : MonoBehaviour
{
    [SerializeField] private NpcController _npcPrefab;
    [SerializeField, Min(1)] private int _capacity = 150;

    private readonly Queue<NpcController> _availableNpcs = new();

    /// <summary>
    /// 고정 용량 NPC를 미리 생성해 비활성 대기열을 준비합니다.
    /// </summary>
    private void Awake()
    {
        CreatePooledNpcs();
    }

    /// <summary>
    /// 비활성 NPC 하나를 대여하며 Pool이 비어도 실행 중 증설하지 않습니다.
    /// </summary>
    public bool TryRentNpc(out NpcController npc)
    {
        while (_availableNpcs.Count > 0)
        {
            npc = _availableNpcs.Dequeue();

            if (npc == null)
            {
                continue;
            }

            return true;
        }

        npc = null;
        return false;
    }

    /// <summary>
    /// 예약이 인계된 대여 NPC를 지정 위치에 배치하고 활성화합니다.
    /// </summary>
    public void Activate(NpcController npc, Vector3 position)
    {
        if (npc == null ||
            !npc.transform.IsChildOf(transform) ||
            _availableNpcs.Contains(npc))
        {
            return;
        }

        npc.transform.position = position;
        npc.gameObject.SetActive(true);
    }

    /// <summary>
    /// Pool 소유 NPC를 정리·비활성화하고 중복 없이 대기열에 반환합니다.
    /// </summary>
    public void Return(NpcController npc)
    {
        if (npc == null ||
            !npc.transform.IsChildOf(transform) ||
            _availableNpcs.Contains(npc))
        {
            return;
        }

        npc.PrepareForPoolReturn();
        npc.gameObject.SetActive(false);
        npc.transform.SetParent(transform);
        _availableNpcs.Enqueue(npc);
    }

    /// <summary>
    /// 설정 용량만큼 NPC를 Pool 자식으로 사전 생성합니다.
    /// </summary>
    private void CreatePooledNpcs()
    {
        if (_npcPrefab == null)
        {
            return;
        }

        int capacity = Mathf.Max(1, _capacity);

        for (int index = 0; index < capacity; index++)
        {
            NpcController npc = Instantiate(_npcPrefab, transform);
            npc.gameObject.SetActive(false);
            _availableNpcs.Enqueue(npc);
        }
    }
}
