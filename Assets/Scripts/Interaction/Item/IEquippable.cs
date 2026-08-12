using UnityEngine;

// 선택 슬롯인 동안 효과가 지속되는 장착형 아이템이 구현한다 (예: 손전등).
// 첫 구현체는 이 리팩터링 스코프 밖 - 손전등 이슈에서 별도로 만든다.
// 디스패치 지점은 PlayerInventory._selectedIndex 변경 시점이 될 예정이다.
public interface IEquippable
{
    void OnEquipped(GameObject user);
    void OnUnequipped(GameObject user);
}
