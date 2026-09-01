using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 막다른 곳에 몰아넣은 참이면 낮은 확률로 그냥 지나칠지.
//
// 사거리에 붙은 뒤에 물어봐야 한다. 그때가 "몰렸다"가 확정되는 순간이라, 그래프에서도
// 이동 노드 다음에 놓여 있다.
//
// 몰림 판정과 주사위는 BossPerception 이 하고 이 노드는 그래프에 노출하는 껍데기다.
// 참이 되는 순간 봐주기 상태가 시작되므로(감각이 둔해진다) 이 조건은 부수효과가 있다.
[Serializable, GeneratePropertyBag]
[Condition(
    name: "Should Spare",
    category: "Boss",
    story: "[Agent] spares the cornered [Survivor]",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70065")]
public partial class ShouldSpareCondition : BossConditionBase
{
    [SerializeReference] public BlackboardVariable<GameObject> Survivor;

    public override bool IsTrue()
        => Survivor?.Value != null
            && TryGetPart(out BossPerception perception)
            && perception.TryGrantMercy(Survivor.Value);
}
