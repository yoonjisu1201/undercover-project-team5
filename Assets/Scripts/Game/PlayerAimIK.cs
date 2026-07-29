using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
// 믹사모에서 다운받은 총 쏘는 애니메이션만으로는 캐릭터 체형에 따라 팔, 손의 위치와 총구가 많이 어긋나기 때문에
// 양 팔이 중앙에 위치하게 하기위해 IK로 위치와 방향을 보정한다.
public sealed class PlayerAimIK : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform gun;
    [SerializeField] private PlayerMoveSample playerMovement;

    [Header("Aim")]
    [SerializeField] private Vector3 gunAimAxis = Vector3.right;
    [SerializeField] private Vector3 gunUpAxis = Vector3.up;
    [SerializeField, Range(0f, 45f)] private float maxAimPitch = 25f;
    [SerializeField, Range(0f, 1f)] private float aimPitchWeight = 0.65f;
    [SerializeField, Min(0f)] private float handPitchOffsetPerDegree = 0.004f;
    [SerializeField, Range(0f, 1f)] private float rotationWeight = 0.9f;
    [SerializeField, Range(-90f, 90f)] private float rightHandRoll = 20f;
    [SerializeField, Range(0f, 1f)] private float positionWeight = 0.9f;
    [SerializeField, Range(0f, 1f)] private float centerPull = 0.65f;
    [SerializeField] private float handSideOffset = 0.08f;
    [SerializeField] private float handRaise = 0.12f;
    [SerializeField] private float handForward = 0.15f;
    [SerializeField] private string activeParameter = "IsUsingArrestTool";

    private Animator animator;
    private int activeParameterHash;
    private bool hasActiveParameter;
    private float currentAimPitch;

    public Vector3 AimDirection => CalculateAimDirection(out _);

    private void Awake()
    {
        animator = GetComponent<Animator>();
        playerMovement ??= GetComponent<PlayerMoveSample>();
        activeParameterHash = Animator.StringToHash(activeParameter);

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == activeParameterHash
                && parameter.type == AnimatorControllerParameterType.Bool)
            {
                hasActiveParameter = true;
                break;
            }
        }

        // 프리팹에서 참조가 비어 있으면 장착된 총을 이름으로 찾는다.
        if (gun == null)
        {
            gun = FindChildByName(transform, "AlienCaptureGun_Visual");
        }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (gun == null
            || gunAimAxis.sqrMagnitude < Mathf.Epsilon
            || gunUpAxis.sqrMagnitude < Mathf.Epsilon)
        {
            return;
        }

        if (hasActiveParameter && !animator.GetBool(activeParameterHash))
        {
            return;
        }

        // 총 프리팹의 로컬 축을 카메라 상하 조준 방향에 맞춘다.
        Vector3 localAim = gunAimAxis.normalized;
        Vector3 localUp = Vector3.ProjectOnPlane(gunUpAxis, localAim).normalized;
        if (localUp.sqrMagnitude < Mathf.Epsilon)
        {
            return;
        }

        Quaternion gunLocalFrame = Quaternion.LookRotation(localAim, localUp);
        Vector3 aimDirection = CalculateAimDirection(out currentAimPitch);
        Quaternion desiredGunFrame = Quaternion.LookRotation(aimDirection, transform.up);
        desiredGunFrame = Quaternion.AngleAxis(rightHandRoll, aimDirection)
            * desiredGunFrame;
        Quaternion desiredGunRotation = desiredGunFrame * Quaternion.Inverse(gunLocalFrame);
        Quaternion targetHandRotation = desiredGunRotation * Quaternion.Inverse(gun.localRotation);

        MoveHandTowardCenter(AvatarIKGoal.LeftHand);
        MoveHandTowardCenter(AvatarIKGoal.RightHand);

        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, rotationWeight);
        animator.SetIKRotation(AvatarIKGoal.RightHand, targetHandRotation);
    }

    private Vector3 CalculateAimDirection(out float pitch)
    {
        float viewPitch = playerMovement != null ? playerMovement.ViewPitch : 0f;
        pitch = Mathf.Clamp(viewPitch, -maxAimPitch, maxAimPitch) * aimPitchWeight;
        return Quaternion.AngleAxis(pitch, transform.right) * transform.forward;
    }

    private void MoveHandTowardCenter(AvatarIKGoal hand)
    {
        HumanBodyBones handBone = hand == AvatarIKGoal.LeftHand
            ? HumanBodyBones.LeftHand
            : HumanBodyBones.RightHand;
        Transform handTransform = animator.GetBoneTransform(handBone);
        if (handTransform == null)
        {
            return;
        }

        // 애니메이션의 손 위치에 중앙 보정과 카메라 상하 조준 높이를 적용한다.
        Vector3 localPosition = transform.InverseTransformPoint(handTransform.position);
        localPosition.x = Mathf.Lerp(localPosition.x, 0f, centerPull) + handSideOffset;
        localPosition.y += handRaise - currentAimPitch * handPitchOffsetPerDegree;
        localPosition.z += handForward;

        animator.SetIKPositionWeight(hand, positionWeight);
        animator.SetIKPosition(hand, transform.TransformPoint(localPosition));
    }

    private static Transform FindChildByName(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName)
            {
                return child;
            }

            Transform match = FindChildByName(child, childName);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }
}
