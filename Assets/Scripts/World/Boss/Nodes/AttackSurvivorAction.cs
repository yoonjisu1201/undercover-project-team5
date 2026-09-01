using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 사거리 안의 생존자를 때린다. 애니메이션이 끝날 때까지 물고 있어서 휘두르는 중에 미끄러지지 않는다.
//
// 쿨다운 중이라도 사거리 안이면 실패하지 않고 기다린다. 실패로 내려가면 아래 가지로 갔다가
// 위 가지의 감시에 걸려 곧바로 되돌아오는 왕복이 생기고, 그때마다 이동 노드가 경로를 버려서
// (ResetPath) 추격이 눈에 띄게 끊긴다. 사거리를 벗어나야 끝난다.
//
// 타격 판정은 BossAttack 이 하고 이 노드는 그래프에 노출하는 껍데기다.
[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Attack Survivor",
    description: "사거리 안의 생존자를 공격한다. 쿨다운 중에는 붙어 있는 동안 기다리고, 사거리를 벗어나면 끝난다.",
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

        if (_attack.TryStartAttack(Survivor.Value))
        {
            return Status.Running;
        }

        // 사거리 밖이라 못 친 것이면 실패로 둬야 위쪽 반복이 이동 노드로 돌아간다.
        return ShouldWaitOutCooldown() ? Status.Running : Status.Failure;
    }

    protected override Status OnUpdate()
    {
        if (_attack == null || Survivor?.Value == null)
        {
            return Status.Success;
        }

        // 공격 상태가 풀리면 애니메이션이 끝난 것이다. BossAttack이 타임아웃도 들고 있어서
        // 애니메이션 이벤트가 오지 않아도 여기서 영원히 멈추지는 않는다.
        if (_attack.IsAttacking)
        {
            return Status.Running;
        }

        // 쿨다운이 끝났고 아직 붙어 있으면 이어서 한 번 더 친다.
        if (_attack.TryStartAttack(Survivor.Value))
        {
            return Status.Running;
        }

        return ShouldWaitOutCooldown() ? Status.Running : Status.Success;
    }

    protected override void OnEnd() => _attack = null;

    private bool ShouldWaitOutCooldown()
        => _attack.IsOnCooldown
            && Survivor?.Value != null
            && _attack.IsInAttackRange(Survivor.Value.transform.position);
}
