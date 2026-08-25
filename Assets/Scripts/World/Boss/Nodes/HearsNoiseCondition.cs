using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;

// 보스가 반경 안의 소음을 들었는지. 들었으면 그 위치를 Blackboard에 남겨서
// 다음 이동 노드가 이어받는다.
//
// 판정은 NoiseSystem이 하고 이 노드는 그래프에 노출하는 껍데기다.
[Serializable, GeneratePropertyBag]
[Condition(
    name: "Hears Noise",
    category: "Boss",
    story: "[Agent] hears a noise within [Radius] into [NoisePosition]",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70011")]
public partial class HearsNoiseCondition : BossConditionBase
{
    [Tooltip("청각 배율. 소음마다 정해진 '들리는 거리'에 이 값을 곱한다.")]
    [SerializeReference] public BlackboardVariable<float> Radius;

    [Tooltip("들은 소음의 위치를 여기에 써 둔다.")]
    [SerializeReference] public BlackboardVariable<Vector3> NoisePosition;

    public override bool IsTrue()
    {
        Transform agent = AgentTransform;
        if (agent == null || !NoiseSystem.TryGetLoudest(agent.position, Radius.Value, out Vector3 position))
        {
            return false;
        }

        if (NoisePosition != null)
        {
            NoisePosition.Value = ToNavigablePoint(position);
        }

        return true;
    }

    // 소음 좌표를 NavMesh 위로 끌어당긴다.
    //
    // 소음은 낸 사람이나 문의 원본 위치라서 NavMesh 밖일 수 있다(문틀, 턱 위 등). 그대로 넘기면
    // 이동 노드의 SetDestination 이 실패해 경로가 PathInvalid 가 된다. 그런데 패키지의
    // Navigate 노드는 PathPartial 일 때만 실패로 빠져나오고 PathInvalid 는 걸러내지 않아서,
    // 그 노드가 영원히 Running 으로 남는다. 목적지가 그대로면 SetDestination 재시도도 없다.
    //
    // 보정에 실패하면 원본을 그대로 넘긴다. 그 경우는 아래 BossController 의 안전망이 받는다.
    private static Vector3 ToNavigablePoint(Vector3 position)
    {
        return NavMesh.SamplePosition(position, out NavMeshHit hit, SampleRadius, NavMesh.AllAreas)
            ? hit.position
            : position;
    }

    // 문 하나 폭 정도. 이보다 멀리서 끌어오면 벽 반대편 통로로 넘어가 엉뚱한 곳을 뒤진다.
    private const float SampleRadius = 3f;
}
