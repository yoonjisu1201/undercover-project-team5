using System;
using Unity.Behavior;
using Unity.Properties;

// 보스가 지금 누군가를 쫓거나 찾고 있는지.
//
// 순간이동 타이머가 반응 트리와 나란히 돌기 때문에, 그냥 두면 추격 도중에 보스가 사라진다.
// 쫓기는 쪽에서는 이유를 알 수 없는 일이라 긴장이 풀린다.
//
// 흔적 하나만 보면 된다. 시야든 근접이든 소리든 단서는 전부 BossTargetMemory 가 흔적으로
// 모으기 때문이다. 예전에는 여기서 소음 목록을 따로 뒤졌는데, 소리가 흔적이 되면서 같은 것을
// 두 번 보는 셈이 됐고 청각 배율도 여기 따로 들고 있어야 했다.
[Serializable, GeneratePropertyBag]
[Condition(
    name: "Is Engaged",
    category: "Boss",
    story: "[Agent] is chasing or searching",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70051")]
public partial class IsEngagedCondition : BossConditionBase
{
    public override bool IsTrue()
        => TryGetPart(out BossTargetMemory memory) && memory.HasMemory;
}
