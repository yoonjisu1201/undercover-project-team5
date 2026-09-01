using System;
using Unity.Behavior;
using Unity.Properties;

// 보스가 수색을 포기하고 쉬는 중인지.
//
// 흔적 주변을 다 뒤졌는데도 못 찾았으면 그 자리에서 곧바로 기본 수색으로 넘어가지 않는다. 잠깐 멈춰
// 숨을 고르는 구간을 둬야, 숨어 있던 쪽에서 "포기했다"를 읽고 움직일 틈이 생긴다.
//
// 판정은 BossTargetMemory 가 하고 이 노드는 그래프에 노출하는 껍데기다.
[Serializable, GeneratePropertyBag]
[Condition(
    name: "Is Resting",
    category: "Boss",
    story: "[Agent] is resting after giving up the search",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70061")]
public partial class IsRestingCondition : BossConditionBase
{
    public override bool IsTrue()
        => TryGetPart(out BossTargetMemory memory) && memory.IsResting;
}
