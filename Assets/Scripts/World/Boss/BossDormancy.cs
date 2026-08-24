using UnityEngine;

// 라운드 초반 보스는 제자리에 잠들어 있다. 누가 가까이 오면 그때 깨어나 움직인다.
//
// 처음부터 쫓아오면 지하에 들어서는 순간 도망만 치게 된다. 잠든 보스를 먼저 마주치게 해서
// "저기 있다"를 알고 들어갈지 말지 고르게 만드는 것이 목적이다.
//
// 한 번 깨면 그 라운드 동안 다시 잠들지 않는다. 보스는 라운드마다 새로 스폰되므로
// 다음 라운드에는 다시 잠든 상태로 시작한다.
//
// 아무도 오지 않으면 정해진 시간 뒤에 스스로 깨어난다. 안 그러면 아무도 그쪽으로 가지 않은
// 라운드에는 보스가 끝까지 서 있어서, 있는지조차 모르고 끝난다.
[RequireComponent(typeof(BossPerception))]
public class BossDormancy : MonoBehaviour
{
    [Tooltip("이 거리 안에 사람이 들어오면 깨어난다. 시야와 무관하게 거리만 본다.")]
    [SerializeField, Min(0f)] private float _wakeRadius = 12f;

    [Tooltip("아무도 오지 않아도 이 시간이 지나면 스스로 깨어나 배회를 시작한다. 0이면 계속 잠들어 있는다.")]
    [SerializeField, Min(0f)] private float _wakeAfterSeconds = 30f;

    [Tooltip("끄면 처음부터 깨어 있는 상태로 시작한다.")]
    [SerializeField] private bool _startDormant = true;

    private BossPerception _perception;
    private bool _isAwake;
    private float _wakeDeadline;

    public bool IsDormant => !_isAwake;

    private void Awake()
    {
        _perception = GetComponent<BossPerception>();
        _isAwake = !_startDormant;
        _wakeDeadline = Time.time + _wakeAfterSeconds;
    }

    // 그래프의 조건 노드가 매 주기 부른다. 아직 자고 있으면 true를 돌려주고,
    // 깨울 조건이 맞으면 그 자리에서 깨운 뒤 false를 돌려준다.
    public bool EvaluateDormant()
    {
        if (_isAwake)
        {
            return false;
        }

        if (_perception.IsSurvivorWithin(_wakeRadius))
        {
            _isAwake = true;
            return false;
        }

        // 아무도 근처에 오지 않아도 언젠가는 움직여야 한다. 계속 서 있으면 지하에 아무 일도
        // 일어나지 않아서, 보스가 있다는 사실 자체를 모르고 라운드가 끝난다.
        if (_wakeAfterSeconds > 0f && Time.time >= _wakeDeadline)
        {
            _isAwake = true;
            return false;
        }

        return true;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = _isAwake ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, _wakeRadius);
    }
}
