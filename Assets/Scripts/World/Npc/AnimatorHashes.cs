using UnityEngine;

// Animator에서 사용하는 NPC 상태, 애니메이션 분기와 Phone 식별자를 한 곳에서 관리합니다.
public static class AnimatorHashes
{
    // NPC 상태 전환과 상태별 애니메이션 선택에 사용하는 파라미터입니다.
    public static readonly int NpcState = Animator.StringToHash("NpcState");
    public static readonly int AnimationVariant = Animator.StringToHash("AnimationVariant");

    // Phone 행동 종료와 Walking While Texting 진입 판정에 사용하는 식별자입니다.
    public static readonly int EndPhoneAction = Animator.StringToHash("EndPhoneAction");
    public static readonly int WalkingWhileTextingStart =
        Animator.StringToHash("Walking While Texting Start");

    // 휴대폰 소품을 유지해야 하는 Animator State Tag입니다.
    public const string PhoneActionTag = "PhoneAction";
}
