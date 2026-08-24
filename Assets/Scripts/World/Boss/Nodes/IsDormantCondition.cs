using System;
using Unity.Behavior;
using Unity.Properties;

// 보스가 아직 잠들어 있는지. 참이면 그래프의 잠복 가지가 돌아 제자리에 서 있는다.
//
// 이 조건이 깨우는 판정까지 겸한다. 잠든 동안 근접 여부를 확인해야 하는 곳이 여기뿐이라
// 별도 감시 노드를 두면 같은 검사를 두 번 하게 된다.
[Serializable, GeneratePropertyBag]
[Condition(
    name: "Is Dormant",
    category: "Boss",
    story: "[Agent] is still dormant",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70031")]
public partial class IsDormantCondition : BossConditionBase
{
    // 컴포넌트가 없으면 잠복 기능을 안 쓰는 것으로 보고 깨어 있게 둔다.
    public override bool IsTrue()
        => TryGetPart(out BossDormancy dormancy) && dormancy.EvaluateDormant();
}
