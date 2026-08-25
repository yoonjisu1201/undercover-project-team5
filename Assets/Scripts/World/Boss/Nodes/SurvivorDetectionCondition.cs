using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// "생존자를 하나 특정했다"는 감지 조건의 공통 부분.
//
// 시야 감지와 근접 감지는 판정 방법만 다르고 이후 처리가 같다 — 찾은 사람을 Blackboard 에 쓰고,
// 놓친 뒤에도 그 자리를 수색할 수 있게 기억에 남긴다. 그 공통 부분을 여기 모았다.
[Serializable, GeneratePropertyBag]
public abstract partial class SurvivorDetectionCondition : BossConditionBase
{
    [Tooltip("찾은 생존자를 여기에 써 둔다. 추격 노드가 이 대상을 따라간다.")]
    [SerializeReference] public BlackboardVariable<GameObject> Survivor;

    // 각 조건이 자기 방식으로 판정한다.
    protected abstract bool TryDetect(BossPerception perception, out GameObject survivor);

    public override bool IsTrue()
    {
        if (!TryGetPart(out BossPerception perception) || !TryDetect(perception, out GameObject survivor))
        {
            return false;
        }

        if (Survivor != null)
        {
            Survivor.Value = survivor;
        }

        // 놓친 뒤에도 마지막 지점을 수색할 수 있게 남겨둔다.
        if (TryGetPart(out BossTargetMemory memory))
        {
            memory.Record(survivor);
        }

        return true;
    }
}
