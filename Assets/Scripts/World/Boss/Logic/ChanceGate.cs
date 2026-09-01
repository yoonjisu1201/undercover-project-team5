using UnityEngine;

// "정해진 간격으로 한 번씩만 굴리고, 성공하면 한동안 다시 굴리지 않는다." 표적 전환과 봐주기가 쓴다.
//
// 간격이 없으면 조건이 맞는 순간 매 프레임 굴려서 확률이 몇이든 즉시 발동하고, 잠금이 없으면
// 성공 직후 또 성공해서 "가끔"이 되지 않는다. 시간과 주사위 값은 인자로 받는다.
public sealed class ChanceGate
{
    private readonly float _rollInterval;
    private readonly float _lockSeconds;

    private float _nextRollTime = float.NegativeInfinity;
    private float _lockedUntil = float.NegativeInfinity;

    /// <param name="rollInterval">주사위를 굴리는 최소 간격(초).</param>
    /// <param name="lockSeconds">성공한 뒤 다시 굴리지 않는 시간(초). 0이면 잠그지 않는다.</param>
    public ChanceGate(float rollInterval, float lockSeconds)
    {
        _rollInterval = rollInterval;
        _lockSeconds = lockSeconds;
    }

    // 성공 뒤 잠금이 풀리기까지 남은 시간(초). 표시용.
    public float LockRemaining(float now) => Mathf.Max(0f, _lockedUntil - now);

    // 다음 주사위까지 남은 시간(초). 표시용.
    public float RollRemaining(float now) => Mathf.Max(0f, _nextRollTime - now);

    /// <param name="roll">0~1 사이의 주사위 값. 호출하는 쪽이 Random.value 를 넘긴다.</param>
    /// <returns>이번에 통과했으면 true. 통과했으면 잠금이 걸린다.</returns>
    public bool TryPass(float now, float chance, float roll)
    {
        if (chance <= 0f || now < _lockedUntil || now < _nextRollTime)
        {
            return false;
        }

        // 굴린 것 자체를 기록한다. 실패해도 간격만큼은 쉬어야 "가끔"이 유지된다.
        _nextRollTime = now + _rollInterval;

        if (roll >= chance)
        {
            return false;
        }

        _lockedUntil = now + _lockSeconds;
        return true;
    }
}
