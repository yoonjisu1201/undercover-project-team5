using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 보스 조건 노드의 공통 부분.
//
// 보스 조건은 전부 "그래프의 Agent 에서 보스 컴포넌트를 찾아 물어본다"는 같은 모양이다.
// 그 앞부분(널 검사 + GetComponent)이 노드마다 반복되므로 여기로 모았다.
// 판정 자체는 각 컴포넌트가 하고, 노드는 그래프에 노출하는 껍데기 역할만 한다.
[Serializable, GeneratePropertyBag]
public abstract partial class BossConditionBase : Condition
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
