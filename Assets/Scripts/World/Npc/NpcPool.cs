using System.Collections.Generic;
using UnityEngine;

public sealed class NpcPool : MonoBehaviour
{
    [SerializeField] private NpcController _npcPrefab;
    [SerializeField, Min(1)] private int _capacity = 150;

    private readonly Queue<NpcController> _availableNpcs = new();

    /// <summary>
    /// 설정된 용량만큼 NPC를 미리 생성하고 비활성 대기열에 보관합니다.
    /// </summary>
    private void Awake()
    {
        Prewarm();
    }

    /// <summary>
    /// 고정 용량 풀에서 비활성 NPC 하나를 꺼냅니다.
    /// </summary>
    /// <param name="npc">대여에 성공한 NPC입니다.</param>
    /// <returns>대여 가능한 NPC가 있으면 true입니다.</returns>
    public bool TryRent(out NpcController npc)
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
    /// 대여한 NPC를 지정한 위치로 옮긴 뒤 활성화합니다.
    /// </summary>
    /// <param name="npc">활성화할 대여 NPC입니다.</param>
    /// <param name="position">배치할 월드 좌표입니다.</param>
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
    /// NPC의 배회 수명을 정리하고 비활성 대기열로 한 번 반환합니다.
    /// </summary>
    /// <param name="npc">반환할 NPC입니다.</param>
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

    private void Prewarm()
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
