using UnityEngine;

// 미션 머신(MissionInteractable) 스폰 설정. 플레이어가 줍는 아이템이 아니라 필드에 고정
// 배치되는 상호작용 기계라서, ItemData(줍는 아이템용)를 재활용하지 않고 따로 둔다.
[CreateAssetMenu(menuName = "Mission/MissionMachineData", fileName = "NewMissionMachineData")]
public class MissionMachineData : ScriptableObject
{
    [SerializeField] private GameObject _worldPrefab;
    [SerializeField] private ItemData _completionReward;

    public GameObject WorldPrefab => _worldPrefab;
    public ItemData CompletionReward => _completionReward;
}
