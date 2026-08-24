using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 보스가 지금 누군가를 쫓거나 찾고 있는지.
//
// 순간이동 타이머가 반응 트리와 나란히 돌기 때문에, 그냥 두면 추격이나 소리 추적 도중에
// 보스가 사라진다. 쫓기는 쪽에서는 이유를 알 수 없는 일이라 긴장이 풀린다.
//
// 시야·근접 감지는 걸리는 순간 기억에 기록되므로 기억 하나로 함께 판정된다.
[Serializable, GeneratePropertyBag]
[Condition(
    name: "Is Engaged",
    category: "Boss",
    story: "[Agent] is chasing or searching within [Radius]",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70051")]
public partial class IsEngagedCondition : BossConditionBase
{
    [Tooltip("청각 배율. 소리 추적 중인지 판정할 때 쓴다. 감지에 쓰는 값과 같게 둔다.")]
    [SerializeReference] public BlackboardVariable<float> Radius;

    public override bool IsTrue()
    {
        Transform agent = AgentTransform;
        if (agent == null)
        {
            return false;
        }

        // 기억이 있으면 추격 중이거나 마지막 지점을 수색하는 중이다.
        if (TryGetPart(out BossTargetMemory memory) && memory.HasMemory)
        {
            return true;
        }

        // 기억이 없어도 소리를 따라가는 중이면 교전으로 본다.
        return NoiseSystem.TryGetLoudest(agent.position, Radius.Value, out _);
    }
}
