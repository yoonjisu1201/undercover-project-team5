using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 지금 지하에서 표적이 될 수 있는 플레이어 목록.
//
// 감지(BossPerception)와 순간이동 거리 계산(BossController)이 각자 접속자 목록을 순회하며
// 같은 조건으로 걸러내고 있었다. 조건이 한쪽만 바뀌면 "감지는 되는데 순간이동은 그 사람을
// 무시한다" 같은 어긋남이 생기므로 한곳에 모았다.
//
// 서버 전용이다. 접속자 목록과 다운·본부 상태는 서버만 정확히 알고, 보스 판정도 서버에서만 돈다.
public static class SurvivorRegistry
{
    // PlayerHealth 조회를 매번 하지 않도록 클라이언트별로 기억해 둔다.
    private static readonly Dictionary<ulong, PlayerHealth> Cache = new();

    // 프레임 단위로 한 번만 만든 목록. 보스 판정이 한 프레임에 여러 번 물어보므로,
    // 그때마다 접속자를 순회하고 반복자를 새로 만들면 그게 그대로 비용이 된다.
    private static readonly List<PlayerHealth> Buffer = new();
    private static int _builtFrame = -1;

    // 살아 있고 지하에 있는 플레이어. 다운된 사람은 은신처에서 소생을 기다리는 중이라 제외한다.
    // 돌려주는 목록은 공용 버퍼이므로 수정하면 안 되고, 다음 프레임에 다시 채워진다.
    public static List<PlayerHealth> Active()
    {
        if (_builtFrame == Time.frameCount)
        {
            return Buffer;
        }

        _builtFrame = Time.frameCount;
        Buffer.Clear();

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsServer)
        {
            return Buffer;
        }

        foreach (NetworkClient client in manager.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null)
            {
                continue;
            }

            if (!Cache.TryGetValue(client.ClientId, out PlayerHealth health) || health == null)
            {
                health = playerObject.GetComponent<PlayerHealth>();
                Cache[client.ClientId] = health;
            }

            if (health == null || health.IsDowned || health.IsInHeadquarters)
            {
                continue;
            }

            Buffer.Add(health);
        }

        return Buffer;
    }

    // 이 오브젝트가 아직 표적이 될 수 있는지. 보스가 들고 있는 표적(기억, 공격 대상)이
    // 그 사이 다운되거나 본부로 빠졌는지 확인할 때 쓴다.
    //
    // 조건을 여기 한곳에 두는 것이 이 클래스의 목적이다. 호출부마다 IsDowned 를 따로 보면
    // "감지에서는 빠졌는데 기억에는 남아 있는" 어긋남이 다시 생긴다.
    public static bool IsActive(GameObject candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        foreach (PlayerHealth survivor in Active())
        {
            if (survivor.gameObject == candidate)
            {
                return true;
            }
        }

        return false;
    }
}
