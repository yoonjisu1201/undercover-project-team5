using Unity.Netcode;

// 단서 아이템. 하나의 ItemData/프리팹을 공유하고, 어떤 단서인지는 서버가 부여한 번호로 구분한다.
// 주우면 인벤토리에 들어가지 않고 번호만 PlayerClueBook에 기록된 뒤 사라진다. 열람은 씬의 단서 목록 UI에서 한다.
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
}
