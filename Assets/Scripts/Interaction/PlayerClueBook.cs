using System;
using Unity.Netcode;
using UnityEngine;

// 플레이어가 획득한 단서 번호 보관소. 단서는 인벤토리 슬롯을 차지하지 않고 여기에만 쌓인다.
// (월드 오브젝트는 획득 즉시 사라지므로, 남는 건 이 번호 목록뿐이다.)
public sealed class PlayerClueBook : NetworkBehaviour
{
    private readonly NetworkList<int> _clueNumbers = new(
        readPerm: NetworkVariableReadPermission.Everyone,
        writePerm: NetworkVariableWritePermission.Server);

    // 단서 목록 UI는 씬에 하나뿐이라, 내 것이 어느 플레이어인지 찾아갈 창구를 둔다.
    public static PlayerClueBook Local { get; private set; }

    public NetworkList<int> ClueNumbers => _clueNumbers;

    // 목록이 바뀔 때마다 UI가 다시 그리도록 알린다.
    public event Action OnClueBookChanged;

    // 새 단서가 들어온 순간에만 발생한다. 획득 토스트처럼 "방금 주웠다"에 반응하는 UI가 쓴다.
    public event Action<int> OnClueAdded;

    public override void OnNetworkSpawn()
    {
        _clueNumbers.OnListChanged += HandleClueNumbersChanged;

        if (IsOwner)
        {
            Local = this;
        }
    }

    public override void OnNetworkDespawn()
    {
        _clueNumbers.OnListChanged -= HandleClueNumbersChanged;

        if (Local == this)
        {
            Local = null;
        }
    }

    private void HandleClueNumbersChanged(NetworkListEvent<int> changeEvent)
    {
        OnClueBookChanged?.Invoke();

        if (changeEvent.Type == NetworkListEvent<int>.EventType.Add)
        {
            OnClueAdded?.Invoke(changeEvent.Value);
        }
    }

    // 서버 전용. 이미 가진 번호면 아무것도 하지 않는다.
    public bool TryAddClueOnServer(int clueNumber)
    {
        if (!IsServer || clueNumber <= 0 || Contains(clueNumber))
        {
            return false;
        }

        _clueNumbers.Add(clueNumber);
        return true;
    }

    public bool Contains(int clueNumber)
    {
        foreach (int number in _clueNumbers)
        {
            if (number == clueNumber)
            {
                return true;
            }
        }

        return false;
    }

    // 라운드가 바뀌면 단서 번호가 새로 배정되므로 이전 라운드 목록은 버린다.
    public void ClearOnServer()
    {
        if (!IsServer)
        {
            return;
        }

        _clueNumbers.Clear();
    }
}
