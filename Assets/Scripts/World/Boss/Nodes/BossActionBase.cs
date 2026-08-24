using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 보스 액션 노드의 공통 부분. BossConditionBase 와 같은 이유로 만들었다.
[Serializable, GeneratePropertyBag]
public abstract partial class BossActionBase : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Agent;

    // 보스의 트랜스폼. Agent 가 비었으면 null.
    protected Transform AgentTransform => Agent?.Value != null ? Agent.Value.transform : null;

    // 보스에 붙은 부품을 꺼낸다. Agent 가 비었거나 부품이 없으면 false.
    protected bool TryGetPart<T>(out T part) where T : Component
    {
        part = null;
        return Agent?.Value != null && Agent.Value.TryGetComponent(out part);
    }
}
