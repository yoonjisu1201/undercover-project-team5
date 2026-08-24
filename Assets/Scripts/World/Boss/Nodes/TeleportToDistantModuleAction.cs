using System;
using Unity.Behavior;
using Unity.Properties;

// 보스를 사람들에게서 가장 먼 모듈로 옮긴다.
//
// 걸어서 이동하면 지하 한쪽 끝에서 반대쪽까지 오는 동안 위협이 사라진다.
// 순간이동으로 "어디 있는지 다시 모르는 상태"를 주기적으로 되돌려서 긴장을 유지한다.
//
// 실제 이동과 소리는 BossController가 하고 이 노드는 그래프에 노출하는 껍데기다.
[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Teleport To Distant Module",
    description: "보스를 생존자들에게서 가장 먼 지하 모듈로 옮긴다.",
    story: "[Agent] teleports to a distant module",
    category: "Boss",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70021")]
public partial class TeleportToDistantModuleAction : BossActionBase
{
    // 옮길 자리를 못 찾으면 실패로 둔다. 이번 주기는 넘기고 다음 주기에 다시 시도한다.
    protected override Status OnStart()
        => TryGetPart(out BossController controller) && controller.TeleportToDistantModule()
            ? Status.Success
            : Status.Failure;
}
