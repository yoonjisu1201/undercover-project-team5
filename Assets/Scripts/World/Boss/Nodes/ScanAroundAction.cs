using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;

// 제자리에서 좌우로 고개를 돌려 주변을 훑는다.
//
// 그냥 기다리기만 하면 도착한 방향 그대로 서 있어서, 시야각(110°) 밖인 등 뒤에 사람이 서 있으면
// 영원히 못 본다. 수색이라는 이름이 무의미해지는 지점이라 실제로 돌게 만든다.
//
// 회전량을 "초당 각도 × 시간"으로 잡으면 시간이 길거나 여러 번 이어질 때 한 바퀴를 넘어 돌아버린다.
// 그래서 최대 각도를 정해두고 그 안에서 좌우로 훑는다. 어떤 경우에도 SweepAngle 을 넘지 않는다.
//
// 회전만 하므로 NavMeshAgent 의 경로는 건드리지 않는다. 서버에서만 돌고 회전은 NetworkTransform 이
// 클라이언트로 동기화한다.
[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Scan Around",
    description: "제자리에서 몸을 돌려 주변을 살핀다. 지정한 시간이 지나면 성공한다.",
    story: "[Agent] scans around for [Duration] seconds",
    category: "Boss",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70041")]
public partial class ScanAroundAction : BossActionBase
{
    [SerializeReference] public BlackboardVariable<float> Duration = new(2f);

    [Tooltip("좌우로 최대 몇 도까지 돌아볼지. 시간이나 속도와 무관하게 이 각도를 넘지 않는다.")]
    [SerializeReference] public BlackboardVariable<float> SweepAngle = new(70f);

    private Transform _agentTransform;
    private NavMeshAgent _navMeshAgent;
    private bool _restoreUpdateRotation;
    private float _startTime;
    private float _duration;
    private float _direction;
    private Quaternion _startRotation;

    protected override Status OnStart()
    {
        _agentTransform = AgentTransform;
        if (_agentTransform == null)
        {
            return Status.Failure;
        }

        _startTime = Time.time;
        _duration = Mathf.Max(0.01f, Duration.Value);
        _startRotation = _agentTransform.rotation;

        // 먼저 어느 쪽을 볼지는 매번 다르게 한다. 항상 같은 방향이면 기계적으로 보인다.
        _direction = UnityEngine.Random.value < 0.5f ? -1f : 1f;

        // NavMeshAgent 가 회전을 관리하면 우리가 돌린 각도를 매 프레임 되돌린다.
        TryGetPart(out _navMeshAgent);
        if (_navMeshAgent != null && _navMeshAgent.updateRotation)
        {
            _navMeshAgent.updateRotation = false;
            _restoreUpdateRotation = true;
        }

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_agentTransform == null)
        {
            return Status.Failure;
        }

        float elapsed = Time.time - _startTime;
        if (elapsed >= _duration)
        {
            // 끝나면 처음 보던 방향으로 정확히 되돌린다. 남은 각도가 누적되면 다음 수색 방향이 틀어진다.
            _agentTransform.rotation = _startRotation;
            return Status.Success;
        }

        // 0 → +각도 → 0 → -각도 → 0. 사인파라 항상 SweepAngle 안에 머물고, 시간이 길어져도
        // 한 바퀴 돌지 않는다. 좌우를 한 번씩 훑고 원래 방향으로 끝난다.
        float phase = elapsed / _duration * Mathf.PI * 2f;
        float sweep = SweepAngle.Value * Mathf.Sin(phase);
        _agentTransform.rotation = _startRotation * Quaternion.Euler(0f, _direction * sweep, 0f);
        return Status.Running;
    }

    protected override void OnEnd()
    {
        // 다음에 이동할 때 다시 진행 방향을 보게 되돌려 놓는다.
        if (_restoreUpdateRotation && _navMeshAgent != null)
        {
            _navMeshAgent.updateRotation = true;
        }

        _restoreUpdateRotation = false;
        _navMeshAgent = null;
        _agentTransform = null;
    }
}
