using UnityEngine;

// 핫바에서 E로 사용하는 소비형 아이템이 구현한다 (예: EnergyBar).
public interface IUsable
{
    // 조준/선택 중일 때 보여줄 문구.
    string UseText { get; }

    // 즉시 사용인지 홀드가 필요한지. 판정 결과와 무관하게 항상 같은 값을 낸다.
    bool RequiresHold { get; }

    // 사용 완료 시 보여줄 문구.
    string UseCompletedMessage { get; }

    // 판정만 한다 - 부작용 없음. 커서 갱신/입력 게이트 양쪽에서 호출된다.
    // false + message => 지금은 못 쓴다(이유 표시). false + null => 처리 안 함.
    bool CanUse(GameObject user, out string message);

    // 서버 전용. 효과를 적용하고, 소모할지 여부도 스스로 정한다 (예: inventory.TryRemoveSelectedItemOnServer 호출).
    void Use(GameObject user, PlayerInventory inventory, int selectedIndex);
}
