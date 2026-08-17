using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 좌하단에 전체 플레이어의 체력바를 표시하는 UI 스크립트.
public sealed class HpStatusUI : MonoBehaviour
{
    [SerializeField] private HpStatusRowUI[] _rows;

    // PlayScene은 라운드 시작 전 전원 스폰이 보장되므로, 시작 시 한 번만 스캔하고 이후 재스캔하지 않는다.
    private readonly List<Player> _players = new();

    private void Start()
    {
        Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
        // OwnerClientId 기준으로 정렬해 모든 클라이언트 화면에 같은 순서로 표시되게 한다.
        Array.Sort(players, (a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        foreach (Player player in players)
        {
            player.PlayerHealth.HpChanged += HandleHpChanged;
            player.PlayerHealth.DownedStateChanged += HandleDownedStateChanged;
            _players.Add(player);
        }

        Render();

        // 도중에 나간 플레이어를 목록에서 빼기 위해 접속 이벤트를 구독한다.
        NetworkManager.Singleton.OnConnectionEvent += HandleConnectionEvent;
    }

    private void OnDestroy()
    {
        foreach (Player player in _players)
        {
            player.PlayerHealth.HpChanged -= HandleHpChanged;
            player.PlayerHealth.DownedStateChanged -= HandleDownedStateChanged;
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnConnectionEvent -= HandleConnectionEvent;
        }
    }

    // 같은 퇴장이라도 호스트에는 ClientDisconnected로, 남은 참가자에게는 PeerDisconnected로 들어온다.
    private void HandleConnectionEvent(NetworkManager networkManager, ConnectionEventData data)
    {
        if (data.EventType != ConnectionEvent.ClientDisconnected &&
            data.EventType != ConnectionEvent.PeerDisconnected) return;

        RemovePlayer(data.ClientId);
    }

    // 나간 플레이어의 체력바가 그대로 남지 않도록 목록에서 빼고 다시 그린다.
    private void RemovePlayer(ulong clientId)
    {
        int index = _players.FindIndex(candidate => candidate != null && candidate.OwnerClientId == clientId);
        if (index < 0) return;

        Player player = _players[index];
        // 이 시점엔 플레이어 오브젝트가 이미 파괴됐을 수 있어 구독 해제 전에 확인한다.
        if (player != null && player.PlayerHealth != null)
        {
            player.PlayerHealth.HpChanged -= HandleHpChanged;
            player.PlayerHealth.DownedStateChanged -= HandleDownedStateChanged;
        }

        _players.RemoveAt(index);
        Render();
    }

    private void HandleHpChanged(float previousValue, float newValue) => Render();
    // #392: DownedStateChanged가 이전/현재 값을 전달하므로 시그니처만 맞추고, UI는 값과 무관하게 다시 그린다.
    private void HandleDownedStateChanged(bool _, bool isDowned) => Render();

    private void Render()
    {
        // _players는 Start()에서 고정된 슬롯 순서이므로 인덱스가 곧 행 번호다.
        for (int i = 0; i < _rows.Length; i++)
        {
            if (i < _players.Count)
            {
                _rows[i].Show(_players[i]);
            }
            else
            {
                _rows[i].Hide();
            }
        }
    }
}
