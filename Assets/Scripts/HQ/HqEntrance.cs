using UnityEngine;

public class HqEntrance : EntranceDoor {
    // 차량 모델의 바퀴+차체 콜라이더를 합친 bounds 중심 하나로 조준 판정을 하다 보니,
    // 오브젝트가 커서 그 중심점을 정확히 조준해야만 상호작용이 잡히는 문제가 있었다.
    // NPC와 마찬가지로 판정 반경을 넉넉하게 잡아준다.
    [SerializeField, Min(1f)] private float _aimRadiusMultiplier = 3f;
    public override float AimRadiusMultiplier => _aimRadiusMultiplier;

    public override string InteractionText => "본부로 이동하기";
}
