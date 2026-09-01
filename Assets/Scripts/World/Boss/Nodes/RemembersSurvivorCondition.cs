using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 보스가 아직 흔적을 쫓고 있는지. 쫓는 동안 향할 지점을 Blackboard에 남긴다.
//
// 추격을 "보이는 동안"이 아니라 "흔적이 살아 있는 동안"으로 묶기 위한 조건이다. 시야가 끊겨도
// 바로 포기하지 않고 흔적까지 간 뒤 그 주변을 뒤지다가, 다 뒤지거나 흔적이 만료되면 끝난다.
//
// 사람을 가리키지 않고 자리만 내보낸다. 흔적을 쫓는 가지는 "누구였는지"를 알 필요가 없고,
// 알게 두면 그 참조로 사람을 직접 따라가는 길이 열린다. 공격에 쓸 대상은 감지 조건
// (Sees Survivor / Survivor Is Near)이 실제로 보고 있을 때만 써 준다.
[Serializable, GeneratePropertyBag]
[Condition(
    name: "Remembers Trace",
    category: "Boss",
    story: "[Agent] remembers a trace at [SearchPosition]",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70032")]
public partial class RemembersSurvivorCondition : BossConditionBase
{
    [Tooltip("지금 향할 지점. 흔적에 닿기 전에는 흔적, 닿은 뒤에는 그 주변의 수색 지점이다.")]
    [SerializeReference] public BlackboardVariable<Vector3> SearchPosition;

    public override bool IsTrue()
    {
        if (!TryGetPart(out BossTargetMemory memory) || !memory.HasMemory)
        {
            return false;
        }

        if (SearchPosition != null)
        {
            SearchPosition.Value = memory.GetSearchPoint();
        }

        return true;
    }
}
