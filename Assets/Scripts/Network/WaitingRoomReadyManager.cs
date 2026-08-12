using System;
using Unity.Netcode;
using UnityEngine;

// 대기방 좌석과 준비 상태를 서버가 관리한다.
// WaitingRoom 씬이 로드될 때마다 (최초 생성/게임 종료 후 재입장) 새로 스폰되며 상태가 자동 초기화된다.
public class WaitingRoomReadyManager : NetworkBehaviour
{
    // 대기룸에서 플레이어 목록UI에 사용할 플레이어 넘버, 준비상태를 나타내는 구조체
    public struct PlayerSlot : IEquatable<PlayerSlot>, INetworkSerializeByMemcpy
    {
        public ulong ClientId;
        public bool IsReady;
        // 슬롯에서 플레이어 편하게 가져올 수 있게 추가한 코드
        public Player Player => NetworkManager.Singleton.ConnectedClients[ClientId].PlayerObject.GetComponent<Player>();

        public bool Equals(PlayerSlot other) =>
            ClientId == other.ClientId && IsReady == other.IsReady;
    }

    public const int MinPlayersToStart = 1;  //최소 시작 인원.  테스트할때는 1, 빌드할때는 3

    private readonly NetworkList<PlayerSlot> _slots = new();

    public NetworkList<PlayerSlot> Slots => _slots;
    // 접속 인원이 최소 인원 이상이고, 방장을 제외한 전원이 준비를 마쳤을 때 시작 가능하다.
    public bool CanStart => HasEnoughPlayers && IsAllReady;
    // 인원수 확인
    public bool HasEnoughPlayers => _slots.Count >= MinPlayersToStart;
    // 전체가 준비했는지 확인
    public bool IsAllReady
    {
        get
        {
            foreach (var slot in _slots)
            {
                if (slot.ClientId == NetworkManager.ServerClientId) continue;
                if (!slot.IsReady) return false;
            }
            return true;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        //방장 먼저 플레이어 목록 추가
        foreach (var clientId in NetworkManager.ConnectedClientsIds)
        {
            AddSlot(clientId);
        }

        NetworkManager.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;

        NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void HandleClientConnected(ulong clientId) => AddSlot(clientId);
    private void HandleClientDisconnected(ulong clientId) => RemoveSlot(clientId);

    // 클라이언트 목록 추가, ID 오름차순으로 정렬 유지 (방장 ID가 0이라 자연히 1번 좌석이 된다).
    private void AddSlot(ulong clientId)
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].ClientId == clientId) return;
        }

        int insertIndex = 0;
        while (insertIndex < _slots.Count && _slots[insertIndex].ClientId < clientId)
        {
            insertIndex++;
        }
        
        _slots.Insert(insertIndex, new PlayerSlot
        {
            ClientId = clientId,
            IsReady = false,
        });
    }

    private void RemoveSlot(ulong clientId)
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].ClientId == clientId)
            {
                _slots.RemoveAt(i);
                return;
            }
        }
    }

    // 준비 버튼(비방장 전용)이 호출한다.
    [Rpc(SendTo.Server)]
    public void SetReadyServerRpc(bool isReady, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (clientId == NetworkManager.ServerClientId) return;

        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].ClientId == clientId)
            {
                var slot = _slots[i];
                slot.IsReady = isReady;
                _slots[i] = slot;
                return;
            }
        }
    }
}
