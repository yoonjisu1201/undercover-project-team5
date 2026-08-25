using UnityEngine;

// 걷기·달리기 발소리를 일정 간격으로 재생하고, 멈추면 재생 중인 소리를 끊는다.
//
// 발소리는 타이머로 재생하는데 애니메이션과 정확히 맞물려 있지 않다. 그래서 멈추는 순간
// 직전에 시작된 소리가 남아 "멈췄는데 한 발 더 걷는" 것처럼 들린다. Stop()이 그 소리를 끊는다.
//
// 플레이어와 보스가 같은 방식으로 발소리를 내므로 한곳에 모아 둔다. 간격은 각자 인스펙터에서 정한다.
public class FootstepLoop
{
    private readonly SoundKey _walkKey;
    private readonly SoundKey _runKey;

    private AudioSource _source;
    private float _timer;

    public FootstepLoop(SoundKey walkKey, SoundKey runKey)
    {
        _walkKey = walkKey;
        _runKey = runKey;
    }

    // 이번 프레임이 한 걸음을 낼 차례인지. true 를 돌려준 뒤에는 호출부가 Play() 를 부르거나,
    // 그냥 넘겨도 된다(공중에 떠 있는 걸음처럼). 타이머는 어느 쪽이든 이미 갱신됐다.
    public bool Tick(bool running, float walkInterval, float runInterval)
    {
        _timer -= Time.deltaTime;
        if (_timer > 0f)
        {
            return false;
        }

        _timer = running ? runInterval : walkInterval;
        return true;
    }

    public void Play(Vector3 position, bool running)
    {
        // 앞 걸음이 아직 울리고 있으면 여기서 끊는다.
        //
        // 클립이 걸음 간격보다 길면(걷기 클립 0.62초 vs 간격 0.45초) 앞 소리가 재생 중이라
        // 풀에서 다른 소스를 잡아가고, 그러면 이 클래스가 들고 있는 소스는 마지막 것 하나뿐이라
        // 멈출 때 앞 걸음 꼬리를 끊지 못한다. 걸음은 어차피 겹치지 않으니 항상 하나만 살려 둔다.
        StopCurrent();

        _source = SoundManager.Instance?.PlayAt(running ? _runKey : _walkKey, position);
    }

    // 멈췄을 때 부른다. 다음 걸음이 곧바로 나도록 타이머도 비운다.
    public void Stop()
    {
        _timer = 0f;
        StopCurrent();
    }

    private void StopCurrent()
    {
        if (_source == null)
        {
            return;
        }

        // 소스는 SoundManager 가 돌려쓰는 풀에서 온 것이라, 그 사이 다른 소리가 차지했을 수 있다.
        // 내가 튼 클립이 아직 재생 중일 때만 끊는다.
        SoundManager manager = SoundManager.Instance;
        if (_source.isPlaying && manager != null &&
            (manager.Owns(_walkKey, _source.clip) || manager.Owns(_runKey, _source.clip)))
        {
            _source.Stop();
        }

        _source = null;
    }
}
