using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 보스가 자기 반경 안의 생존자를 알아챘는지. 시야각을 보지 않으므로 등 뒤도 걸린다.
// 손전등을 켜고 다니는 것도 이 판정에 함께 걸린다.
[Serializable, GeneratePropertyBag]
[Condition(
    name: "Survivor Is Near",
    category: "Boss",
    story: "[Agent] senses a survivor nearby into [Survivor]",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70013")]
public partial class SurvivorIsNearCondition : SurvivorDetectionCondition
{
    protected override bool TryDetect(BossPerception perception, out GameObject survivor)
        => perception.TryGetNearbySurvivor(out survivor);
}
