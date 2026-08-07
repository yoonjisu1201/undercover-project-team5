// PlayerItemIK가 우선순위에 따라 어느 손 IK를 적용할지 판단하기 위한 공통 인터페이스.

using System;
using UnityEngine;

public abstract class HandIKBase : MonoBehaviour {
    
    protected Animator _animator;
    
    public abstract bool IsActive { get; }
    public abstract void ApplyIK(int layerIndex);

    protected virtual void Awake() {
        _animator = GetComponent<Animator>();
    }
}
