using System;
using System.Collections.Generic;
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
    }

    private void OnDestroy()
    {
        foreach (Player player in _players)
        {
            player.PlayerHealth.HpChanged -= HandleHpChanged;
            player.PlayerHealth.DownedStateChanged -= HandleDownedStateChanged;
        }
    }

    private void HandleHpChanged(float previousValue, float newValue) => Render();
    private void HandleDownedStateChanged(bool isDowned) => Render();

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
