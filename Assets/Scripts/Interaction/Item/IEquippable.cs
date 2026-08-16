using UnityEngine;

// 선택 슬롯인 동안 효과가 지속되는 장착형 아이템이 구현한다 (예: 손전등).
public interface IEquippable
{
    void OnEquipped(GameObject user);
    void OnUnequipped(GameObject user);
}
