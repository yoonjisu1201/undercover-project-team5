using System.Collections.Generic;
using UnityEngine;

// 보스는 "무슨 소리가 어디서 났는지"만 알면 되므로 실제 음향 전파는 계산하지 않는다.
// 최근 소음을 좌표와 들리는 거리로만 들고 있고, 보스 감지가 그것을 조회한다.
//
// 누가 냈는지는 일부러 기록하지 않는다. 보스가 사람이 아니라 위치를 쫓아야 공포가 유지되고,
// 남의 근처에서 일부러 소리를 내는 플레이도 성립한다.
//
// 서버 전용이다. 소음을 보고하는 쪽과 조회하는 쪽 모두 서버에서만 동작하므로 복제하지 않는다.
public static class NoiseSystem
{
    // 소음이 남아 있는 시간(소리 잔상).
    //
    // 이 값이 곧 "지나간 뒤 얼마나 오래 흔적이 남는가"다. 3초로 두면 뛰어서 지나간 자리가
    // 한참 뒤까지 보스에게 읽혀서, 계속 달려도 뒤에 자국이 이어지듯 따라붙었다.
    // 짧게 두면 소리를 낸 그 순간 근처에 있어야만 들리고, 지나가면 자국이 곧바로 사라진다.
    //
    // 너무 짧으면 먼 곳에서 난 소리를 보스가 알아채기도 전에 없어진다. 사람이 소음을 보고하는
    // 간격(0.4초)의 몇 배는 남겨서, 한 번 난 소리를 놓치지는 않게 한다.
    private const float Lifetime = 3f;

    public readonly struct Noise
    {
        public readonly Vector3 Position;
        public readonly float Radius;      // 이 거리 안에서는 들린다
        public readonly float ExpireTime;

        // 무슨 소리였는지. 디버그 표시에만 쓴다. 누가 냈는지는 여전히 기록하지 않는다.
        public readonly string Kind;

        public Noise(Vector3 position, float radius, float expireTime, string kind)
        {
            Position = position;
            Radius = radius;
            ExpireTime = expireTime;
            Kind = kind;
        }
    }

    private static readonly List<Noise> Noises = new();

    // 디버그 표시용. 만료된 것이 섞여 있을 수 있으니 ExpireTime을 보고 걸러 쓴다.
    public static IReadOnlyList<Noise> ActiveNoises => Noises;

    // radius는 "이 소음이 들리는 거리(m)". 달리기처럼 큰 소리는 크게, 걷기는 작게 준다.
    // kind는 디버그 표시용 이름이라 판정에는 쓰이지 않는다.
    public static void Report(Vector3 position, float radius, string kind = "소음")
    {
        if (radius <= 0f)
        {
            return;
        }

        Noises.Add(new Noise(position, radius, Time.time + Lifetime, kind));
    }

    // 듣는 쪽에서 가장 두드러지는 소음 하나를 고른다.
    // hearingFactor는 보스의 청각 배율(긴장도에 따라 올라간다).
    public static bool TryGetLoudest(Vector3 listenerPosition, float hearingFactor, out Vector3 noisePosition)
    {
        Prune();

        noisePosition = default;
        float bestMargin = 0f;
        bool found = false;

        for (int i = 0; i < Noises.Count; i++)
        {
            Noise noise = Noises[i];
            float audibleRange = noise.Radius * hearingFactor;

            // 거리에 비해 얼마나 크게 들리는지. 같은 크기여도 가까운 쪽이 우선한다.
            float margin = audibleRange - Vector3.Distance(listenerPosition, noise.Position);
            if (margin <= 0f || margin <= bestMargin)
            {
                continue;
            }

            bestMargin = margin;
            noisePosition = noise.Position;
            found = true;
        }

        return found;
    }

    // 라운드가 바뀌면 이전 라운드의 소음이 새 지하 맵으로 넘어오지 않게 비운다.
    public static void Clear()
    {
        Noises.Clear();
    }

    private static void Prune()
    {
        for (int i = Noises.Count - 1; i >= 0; i--)
        {
            if (Noises[i].ExpireTime <= Time.time)
            {
                Noises.RemoveAt(i);
            }
        }
    }
}
