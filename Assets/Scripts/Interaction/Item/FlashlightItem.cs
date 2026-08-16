using Unity.Netcode;
using UnityEngine;

// 인벤토리에 들어가는 손전등 인스턴스가 점등 상태를 소유한다.
// 선택과 E 입력은 각각 IEquippable과 IUsable로 받고, 손에 보이는 모델과 광원은 PlayerItemIK에 맡긴다.
public class FlashlightItem : ItemBase, IEquippable, IUsable
{
    // 새 손전등은 켜진 상태로 시작하며 서버만 값을 바꿀 수 있다.
    // 모든 피어가 같은 값을 읽으므로 소유자와 원격 플레이어의 손전등 표시가 동일하게 갱신된다.
    private readonly NetworkVariable<bool> _isOn = new(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // 이 아이템이 현재 선택된 플레이어의 손전등 표시를 가리킨다.
    // 선택이 해제되면 참조를 비워 이후 상태 동기화가 장착 해제된 표시에 영향을 주지 않게 한다.
    private PlayerItemIK _equippedItemIK;

    // 현재 상태와 반대 동작을 안내해야 하므로 켜져 있으면 "끄기", 꺼져 있으면 "켜기"를 표시한다.
    public string UseText => _isOn.Value ? "손전등 끄기" : "손전등 켜기";

    // 이 문구는 Use 이후에 읽히므로 토글된 결과 상태를 기준으로 완료 동작을 설명한다.
    public string UseCompletedMessage => _isOn.Value ? "손전등을 켰습니다." : "손전등을 껐습니다.";

    // 네트워크 스폰이 끝난 뒤 점등 값 변경을 구독해 서버의 토글 결과를 각 피어의 손 모델에 반영한다.
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _isOn.OnValueChanged += HandleLightStateChanged;
    }

    // 디스폰되는 아이템이 남긴 구독과 손전등 표시를 정리한 뒤 ItemBase의 디스폰 처리를 이어간다.
    public override void OnNetworkDespawn()
    {
        _isOn.OnValueChanged -= HandleLightStateChanged;

        HideEquippedFlashlight();

        base.OnNetworkDespawn();
    }

    // 손전등은 충전량이나 재사용 대기시간 없이 언제든 토글할 수 있으므로 실패 조건을 두지 않는다.
    public bool CanUse(GameObject user, out string failReason)
    {
        // null은 사용자에게 표시할 사용 실패 사유가 없다는 뜻이다.
        failReason = null;
        return true;
    }

    // PlayerItemUse의 서버 검증을 통과한 뒤 서버에서 점등 상태를 반전한다.
    // 변경된 NetworkVariable 값은 OnValueChanged를 통해 모든 피어의 장착 표시로 전달된다.
    public void Use(GameObject user, PlayerInventory inventory, int selectedIndex)
    {
        _isOn.Value = !_isOn.Value;
    }

    // 선택 슬롯에 들어오면 해당 플레이어의 IK 표시를 연결하고 현재 동기화된 점등 상태로 장착한다.
    public void OnEquipped(GameObject user)
    {
        // user는 이 아이템을 선택한 PlayerInventory와 같은 플레이어 오브젝트다.
        _equippedItemIK = user.GetComponent<PlayerItemIK>();

        // 뒤늦게 장착해도 저장된 On/Off 상태가 그대로 복원된다.
        _equippedItemIK.EquipFlashlight(_isOn.Value);
    }

    // 다른 슬롯을 선택하거나 아이템이 슬롯에서 빠지면 이 인스턴스가 켜 둔 손전등 표시를 해제한다.
    public void OnUnequipped(GameObject user)
    {
        HideEquippedFlashlight();
    }

    // 서버에서 동기화된 새 점등 값을 현재 장착 중인 손전등 표시에만 전달한다.
    private void HandleLightStateChanged(bool previousValue, bool currentValue)
    {
        _equippedItemIK?.SetFlashlightOn(currentValue);
    }

    // 장착 중일 때만 손 모델과 광원을 숨기고 연결 참조를 해제한다.
    private void HideEquippedFlashlight()
    {
        if (_equippedItemIK == null)
        {
            return;
        }

        _equippedItemIK.UnequipFlashlight();

        // 이후 OnValueChanged가 와도 이미 해제된 플레이어 표시를 다시 조작하지 않게 한다.
        _equippedItemIK = null;
    }
}
