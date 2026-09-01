using UnityEngine;

// 흔적의 수명과 갱신 규칙만 담는다. 좌표는 모르고 "언제까지 유효한가"만 안다.
//
// MonoBehaviour 가 아니고 Time.time 도 읽지 않는다. 시간을 인자로 받으므로 플레이 모드 없이
// 규칙을 확인할 수 있다. 실시간 추적·무한 연장·오래된 소리가 흔적을 끌어당기는 문제가
// 전부 여기 시간 계산에서 났다.
public sealed class TraceLifetime
{
    private readonly float _sightSeconds;
    private readonly float _noiseSeconds;
    private readonly float _searchSeconds;
    private readonly float _noiseInterval;
    private readonly float _sightHoldSeconds;

    private bool _hasTrace;
    private bool _fromNoise;
    private float _forgetTime;
    private float _lastSightTime = float.NegativeInfinity;
    private float _nextNoiseTime = float.NegativeInfinity;

    /// <param name="sightSeconds">눈으로 본 흔적의 수명.</param>
    /// <param name="noiseSeconds">소리로 생긴 흔적의 수명. 소리 난 곳까지 걸어갈 시간은 줘야 한다.</param>
    /// <param name="searchSeconds">흔적에 도착한 뒤 주변을 뒤지는 데 주는 시간.</param>
    /// <param name="noiseInterval">소리로 흔적을 옮기는 최소 간격. 이게 실시간 추적을 막는다.</param>
    /// <param name="sightHoldSeconds">눈으로 본 직후 이 시간 동안은 소리가 흔적을 덮지 않는다.</param>
    public TraceLifetime(
        float sightSeconds, float noiseSeconds, float searchSeconds,
        float noiseInterval, float sightHoldSeconds)
    {
        _sightSeconds = sightSeconds;
        _noiseSeconds = noiseSeconds;
        _searchSeconds = searchSeconds;
        _noiseInterval = noiseInterval;
        _sightHoldSeconds = sightHoldSeconds;
    }

    // 이 흔적이 소리에서 왔는지. 표시용이고 판정에는 쓰지 않는다.
    public bool FromNoise => _fromNoise;

    public bool IsAlive(float now) => _hasTrace && now < _forgetTime;

    // 남은 수명(초). 표시용.
    public float RemainingSeconds(float now) => _hasTrace ? Mathf.Max(0f, _forgetTime - now) : 0f;

    // 다음 소리 갱신이 가능해지기까지 남은 시간(초). 표시용.
    public float NoiseGateRemaining(float now) => Mathf.Max(0f, _nextNoiseTime - now);

    // 눈으로 봤다. 보이는 동안 매 주기 불리므로 수명이 계속 갱신된다.
    public void RecordSight(float now)
    {
        _hasTrace = true;
        _fromNoise = false;
        _lastSightTime = now;
        _forgetTime = now + _sightSeconds;

        // 시야가 끊긴 직후 한 박자는 마지막으로 본 자리로 향한다. 그 뒤부터 소리가 이어받는다.
        _nextNoiseTime = now + _noiseInterval;
    }

    // 소리를 들었다. 흔적을 그쪽으로 옮겨도 되면 true.
    //
    // 만료 시각을 소리 쪽 수명으로 "다시" 잡는다. 늘리는 것이 아니라 새로 정하는 것이라,
    // 소리를 계속 내도 추격이 무한정 이어지지는 않는다.
    public bool TryRecordNoise(float now)
    {
        if (now - _lastSightTime < _sightHoldSeconds || now < _nextNoiseTime)
        {
            return false;
        }

        _hasTrace = true;
        _fromNoise = true;
        _forgetTime = now + _noiseSeconds;
        _nextNoiseTime = now + _noiseInterval;
        return true;
    }

    // 흔적에 도착했다. 뒤질 시간을 새로 준다.
    //
    // 쫓아가는 시간과 뒤지는 시간이 같은 타이머를 쓰면, 걸어가는 데 쓴 만큼 수색할 시간이
    // 그대로 깎인다. 멀리서 놓칠수록 수색을 못 하게 되는데, 정작 그때 더 뒤져야 한다.
    //
    // 남은 수명이 이미 더 길면 줄이지 않는다.
    public void BeginSearch(float now)
    {
        if (!_hasTrace)
        {
            return;
        }

        _forgetTime = Mathf.Max(_forgetTime, now + _searchSeconds);
    }

    // 흔적을 버린다. 다음 소리는 곧바로 받을 수 있어야 하므로 간격 제한도 함께 푼다.
    public void Clear()
    {
        _hasTrace = false;
        _fromNoise = false;
        _lastSightTime = float.NegativeInfinity;
        _nextNoiseTime = float.NegativeInfinity;
    }
}
