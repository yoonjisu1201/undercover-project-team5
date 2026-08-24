using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 사거리 안의 생존자를 때린다. 애니메이션이 끝날 때까지 이 노드가 물고 있어서,
// 휘두르는 중에 보스가 계속 미끄러져 오지 않는다.
//
// 사거리 밖이거나 쿨다운이면 실패한다. 위쪽 반복이 다시 돌면서 이동 노드로 돌아간다.
//
// 실제 타격 판정과 피해는 BossAttack이 하고 이 노드는 그래프에 노출하는 껍데기다.
[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Attack Survivor",
    description: "사거리 안의 생존자를 공격한다. 사거리 밖이거나 쿨다운이면 실패한다.",
    story: "[Agent] attacks [Survivor]",
    category: "Boss",
    id: "b0551ee1f4c14ab0a1e3d5c9f8a70022")]
public partial class AttackSurvivorAction : BossActionBase
{
    [SerializeReference] public BlackboardVariable<GameObject> Survivor;

    private BossAttack _attack;

    protected override Status OnStart()
    {
        if (Survivor?.Value == null || !TryGetPart(out _attack))
        {
            return Status.Failure;
        }

        return _attack.TryStartAttack(Survivor.Value) ? Status.Running : Status.Failure;
    }

    // 공격 상태가 풀리면 애니메이션이 끝난 것이다. BossAttack이 타임아웃도 들고 있어서
    // 애니메이션 이벤트가 오지 않아도 여기서 영원히 멈추지는 않는다.
    protected override Status OnUpdate()
        => _attack == null || !_attack.IsAttacking ? Status.Success : Status.Running;

    protected override void OnEnd() => _attack = null;
}
