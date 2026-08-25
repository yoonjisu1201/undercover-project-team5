using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

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
            NoisePosition.Value = position;
        }

        return true;
    }
}
