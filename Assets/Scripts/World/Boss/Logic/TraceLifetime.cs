using UnityEngine;

// 소리가 흔적을 옮겨도 되는지만 판단한다. 흔적의 수명은 다루지 않는다.
//
// 예전에는 여기서 만료 시각도 들고 있었다. 그런데 흔적까지는 무조건 걸어가고 수색은 정해진
// 횟수만큼만 하므로, 시간으로 끊을 자리가 없다. 시간이 남아 있으면 시간대로 끊기고 횟수가
// 남아 있으면 횟수대로 끊겨서, 밖에서는 이유 없이 수색을 접는 것으로 보였다.
//
// MonoBehaviour 가 아니고 Time.time 도 읽지 않는다. 시간을 인자로 받으므로 플레이 모드 없이
// 규칙을 확인할 수 있다.
public sealed class TraceLifetime
{
    private readonly float _noiseInterval;
    private readonly float _sightHoldSeconds;

    private bool _fromNoise;
    private float _lastSightTime = float.NegativeInfinity;
    private float _nextNoiseTime = float.NegativeInfinity;

    /// <param name="noiseInterval">소리로 흔적을 옮기는 최소 간격. 이게 실시간 추적을 막는다.</param>
    /// <param name="sightHoldSeconds">눈으로 본 직후 이 시간 동안은 소리가 흔적을 덮지 않는다.</param>
    public TraceLifetime(float noiseInterval, float sightHoldSeconds)
    {
        _noiseInterval = noiseInterval;
        _sightHoldSeconds = sightHoldSeconds;
    }

    // 이 흔적이 소리에서 왔는지. 표시용이고 판정에는 쓰지 않는다.
    public bool FromNoise => _fromNoise;

    // 다음 소리 갱신이 가능해지기까지 남은 시간(초). 표시용.
    public float NoiseGateRemaining(float now) => Mathf.Max(0f, _nextNoiseTime - now);

    // 눈으로 봤다. 보이는 동안 매 주기 불린다.
    public void RecordSight(float now)
    {
        _fromNoise = false;
        _lastSightTime = now;

        // 시야가 끊긴 직후 한 박자는 마지막으로 본 자리로 향한다. 그 뒤부터 소리가 이어받는다.
        _nextNoiseTime = now + _noiseInterval;
    }

    // 소리를 들었다. 흔적을 그쪽으로 옮겨도 되면 true.
    public bool TryRecordNoise(float now)
    {
        if (now - _lastSightTime < _sightHoldSeconds || now < _nextNoiseTime)
        {
            return false;
        }

        _fromNoise = true;
        _nextNoiseTime = now + _noiseInterval;
        return true;
    }

    // 흔적을 버린다. 다음 소리는 곧바로 받을 수 있어야 하므로 간격 제한도 함께 푼다.
    public void Clear()
    {
        _fromNoise = false;
        _lastSightTime = float.NegativeInfinity;
        _nextNoiseTime = float.NegativeInfinity;
    }
}
