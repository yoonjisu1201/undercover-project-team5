using System;
using Unity.Behavior;
using Unity.Properties;

// 쫓던 것을 놓는다. 흔적을 버리고 포기 직후의 정지 구간을 연다.
//
// 감각만 둔해지게 두고 흔적을 남기면, 잠시 뒤 마지막으로 본 자리로 다시 걸어온다.
// 봐주기가 봐주기가 되려면 흔적까지 버려야 한다.
[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Give Up Chase",
    description: "흔적을 버리고 포기 상태로 들어간다. 항상 성공한다.",
    story: "[Agent] gives up the chase",
    category: "Boss",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70066")]
public partial class GiveUpChaseAction : BossActionBase
{
    protected override Status OnStart()
    {
        if (TryGetPart(out BossTargetMemory memory))
        {
            memory.GiveUp();
        }

        return Status.Success;
    }
}
