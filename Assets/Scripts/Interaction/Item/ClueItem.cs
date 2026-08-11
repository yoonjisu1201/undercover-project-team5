using Unity.Netcode;
using UnityEngine;

// 단서 아이템. ItemData/프리팹 하나를 모든 단서가 공유하고, 몇 번 단서인지는 스폰 직후
// 서버(ClueSpawner/MissionInteractable)가 부여하는 _clueNumber(1-based)로 구분한다.
public class ClueItem : ItemBase
{
    private readonly NetworkVariable<int> _clueNumber =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int ClueNumber => _clueNumber.Value;

    // 서버 전용. NetworkObject.Spawn() 이후에 호출해야 한다.
    public void SetClueNumber(int clueNumber)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        _clueNumber.Value = clueNumber;
    }

    protected override void OnAdded()
    {
        ShowClue();
    }

    public override void OnSelected()
    {
        ShowClue();
    }

    private void ShowClue()
    {
        int clueIndex = _clueNumber.Value - 1;
        if (clueIndex < 0)
        {
            return;
        }

        ClueUI[] clueDisplays = FindObjectsByType<ClueUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        System.Array.Sort(clueDisplays, (left, right) => string.CompareOrdinal(left.name, right.name));

        if (clueIndex >= clueDisplays.Length)
        {
            Debug.LogWarning($"표시할 ClueDisplay가 부족합니다. 단서 번호: {_clueNumber.Value}");
            return;
        }

        clueDisplays[clueIndex].gameObject.SetActive(true);
    }
}
