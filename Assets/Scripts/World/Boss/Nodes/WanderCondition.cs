using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 단서가 없을 때 향할 지점. 항상 참이라 반응 트리의 마지막 가지로 쓰인다.
//
// 순찰(Patrol)을 대체했다. 순찰은 목적지가 미리 정해져 있어서 보스가 그 점까지 한 번에 걸어가
// 버리는데, 그 직선이 숨은 사람 쪽이면 단서 없이 찾아오는 것처럼 보인다. 여기서는 한 걸음
// (6~12m)씩만 정해서 그런 직선이 생기지 않는다. 지점 선택은 BossTargetMemory 가 한다.
[Serializable, GeneratePropertyBag]
[Condition(
    name: "Wander",
    category: "Boss",
    story: "[Agent] wanders toward [WanderPosition]",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70064")]
public partial class WanderCondition : BossConditionBase
{
    [Tooltip("지금 향할 지점. 진행 방향 앞쪽에서 한 걸음 거리로 뽑힌다.")]
    [SerializeReference] public BlackboardVariable<Vector3> WanderPosition;

    public override bool IsTrue()
    {
        if (WanderPosition == null)
        {
            return true;
        }

        if (TryGetPart(out BossTargetMemory memory))
        {
            WanderPosition.Value = memory.GetWanderPoint();
        }

        return true;
    }
}
