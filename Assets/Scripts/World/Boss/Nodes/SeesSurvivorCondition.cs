using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 보스가 시야 안에서 생존자를 봤는지. 멀리서는 보고 있어야만 걸린다.
[Serializable, GeneratePropertyBag]
[Condition(
    name: "Sees Survivor",
    category: "Boss",
    story: "[Agent] has detected a survivor into [Survivor]",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70012")]
public partial class SeesSurvivorCondition : SurvivorDetectionCondition
{
    protected override bool TryDetect(BossPerception perception, out GameObject survivor)
        => perception.TryGetVisibleSurvivor(out survivor);
}
