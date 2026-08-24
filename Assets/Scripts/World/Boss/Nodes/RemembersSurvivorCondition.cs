using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 보스가 아직 마지막으로 본 생존자를 기억하는지. 기억하는 동안 수색 지점을 Blackboard에 남긴다.
//
// 추격을 "보이는 동안"이 아니라 "기억하는 동안"으로 묶기 위한 조건이다. 시야가 끊겨도 바로
// 포기하지 않고 마지막 지점을 수색하다가, 기억이 만료될 때 비로소 조우가 끝난다.
[Serializable, GeneratePropertyBag]
[Condition(
    name: "Remembers Survivor",
    category: "Boss",
    story: "[Agent] remembers [Survivor] near [SearchPosition]",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70032")]
public partial class RemembersSurvivorCondition : BossConditionBase
{
    [SerializeReference] public BlackboardVariable<GameObject> Survivor;

    [Tooltip("지금 살펴볼 지점. 마지막 목격 지점에서 달아난 방향으로 전진하며 이어진다.")]
    [SerializeReference] public BlackboardVariable<Vector3> SearchPosition;

    public override bool IsTrue()
    {
        if (!TryGetPart(out BossTargetMemory memory) || !memory.HasMemory)
        {
            return false;
        }

        if (Survivor != null)
        {
            Survivor.Value = memory.Survivor;
        }

        if (SearchPosition != null)
        {
            SearchPosition.Value = memory.GetSearchPoint();
        }

        return true;
    }
}
