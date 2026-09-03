using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
// 믹사모에서 다운받은 총 쏘는 애니메이션만으로는 캐릭터 체형에 따라 팔, 손의 위치와 총구가 많이 어긋나기 때문에
// 양 팔이 중앙에 위치하게 하기위해 IK로 위치와 방향을 보정한다.
public sealed class PlayerAimIK : MonoBehaviour, IHandIK
{
    [Header("References")]
    [SerializeField] private Transform gun;
    [SerializeField] private PlayerCameraController playerCameraController;

    private Animator _animator;

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

    // 가중치를 0 에서 1 로 한 프레임에 올리면 팔이 조준 자세로 순간이동한다. 뺄 때도 마찬가지로
    // 호출이 끊기는 순간 원래 애니메이션으로 튄다. 양쪽 다 이 시간에 걸쳐 서서히 섞는다.
    [SerializeField, Min(0f)] private float blendDuration = 0.18f;

    // 되돌아가는 구간의 출발점만 옆으로 민다. 도착점은 아이템 자세 그대로라 정착 위치는 안 변한다.
    [Tooltip("제압기를 놓고 되돌아갈 때 시작 자세를 옆으로 미는 양(m). 양수면 오른쪽")]
    [SerializeField] private float returnStartSideOffset = 0.2f;
    
    private int activeParameterHash;
    private bool hasActiveParameter;
    private float currentAimPitch;
    private float blend;

    // 손을 뗀 뒤의 조준 목표. 매 프레임 다시 계산하면 안 되기 때문에 잡아 둔다.
    //
    // 위치 목표는 현재 애니메이션 손 위치에서 뽑는데, 상체 레이어가 0.1초면 Empty 로 넘어가는 반면
    // 이 블렌드는 0.18초라, 남은 구간에서 목표가 "빈손 자세 + 오프셋"이라는 엉뚱한 점으로 옮겨간다.
    // 손이 그 점을 들렀다 온다. 회전 목표는 카메라만 보고 계산해서 그런 일이 없으니, 위치만 흔들리고
    // 회전은 끝까지 남아 따로 되돌아가는 것처럼 보인다. 놓는 순간의 목표를 얼려 두면 둘이 같이 온다.
    private Vector3 frozenLeftHandPosition;
    private Vector3 frozenRightHandPosition;
    private Quaternion frozenHandRotation = Quaternion.identity;

    // 한 번도 잡아 두지 않은 얼린 값은 원점(발밑)이라, 그대로 쓰면 양손이 발밑으로 순간이동한다.
    // 도구를 든 프레임이 한 번은 지나야 쓸 수 있다.
    private bool hasFrozenGoal;

    public Vector3 AimDirection => CalculateAimDirection(out _);

    // PlayerItemIK가 이 값을 보고 지금 이 IK를 적용할지 판단한다.
    // 도구를 내린 뒤에도 blend 가 남아 있는 동안은 계속 적용해야 서서히 풀린다.
    public bool IsActive =>
        gun != null
        && gunAimAxis.sqrMagnitude >= Mathf.Epsilon
        && gunUpAxis.sqrMagnitude >= Mathf.Epsilon
        && (IsToolHeld || blend > 0f);

    private bool IsToolHeld => !hasActiveParameter || _animator.GetBool(activeParameterHash);

    private void Update()
    {
        float step = blendDuration > 0f ? Time.deltaTime / blendDuration : 1f;
        blend = Mathf.MoveTowards(blend, IsToolHeld ? 1f : 0f, step);
    }

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        playerCameraController ??= GetComponent<PlayerCameraController>();
        activeParameterHash = Animator.StringToHash(activeParameter);

        foreach (AnimatorControllerParameter parameter in _animator.parameters)
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

    // PlayerItemIK가 IsActive를 확인한 뒤 호출한다.
    public void ApplyIK(int layerIndex)
    {
        // 총 프리팹의 로컬 축을 카메라 상하 조준 방향에 맞춘다.
        Vector3 localAim = gunAimAxis.normalized;
        Vector3 localUp = Vector3.ProjectOnPlane(gunUpAxis, localAim).normalized;
        if (localUp.sqrMagnitude < Mathf.Epsilon)
        {
            return;
        }

        // 얼린 값이 아직 없으면 놓은 상태라도 새로 계산한다. 놓자마자 첫 호출이 오는
        // 경우(도구 플래그가 한 프레임 안에서 켜졌다 꺼짐)에 원점으로 튀는 것을 막는다.
        bool held = IsToolHeld || !hasFrozenGoal;
        Quaternion targetHandRotation;
        if (held)
        {
            Quaternion gunLocalFrame = Quaternion.LookRotation(localAim, localUp);
            Vector3 aimDirection = CalculateAimDirection(out currentAimPitch);
            Quaternion desiredGunFrame = Quaternion.LookRotation(aimDirection, transform.up);
            desiredGunFrame = Quaternion.AngleAxis(rightHandRoll, aimDirection)
                * desiredGunFrame;
            Quaternion desiredGunRotation = desiredGunFrame * Quaternion.Inverse(gunLocalFrame);
            targetHandRotation = desiredGunRotation * Quaternion.Inverse(gun.localRotation);
            frozenHandRotation = targetHandRotation;
            hasFrozenGoal = true;
        }
        else
        {
            targetHandRotation = frozenHandRotation;
        }

        MoveHandTowardCenter(AvatarIKGoal.LeftHand, held);
        MoveHandTowardCenter(AvatarIKGoal.RightHand, held);

        // 앞서 아이템 IK 가 잡아 둔 자세를 출발점으로 삼아 조준 자세로 섞는다. 0 으로 빼 버리면
        // 팔이 빈손 애니메이션으로 갔다가 아이템 자세로 다시 튄다.
        //
        // 다만 바탕이 실제로 깔려 있을 때만이다. 아이템 IK 가 가중치 0 으로 꺼 두면 그 목표값은
        // 지난 프레임 찌꺼기라 아무 자리도 가리키지 않는다. 그걸 출발점으로 섞으면 손이 엉뚱한
        // 유령 목표에 한 번 끌려갔다가 애니메이션 자세로 내려온다.
        // 가중치도 바탕에서 조준 값으로 이어서 섞는다. Max 로 집으면 다 섞인 지점에서
        // 값이 한 번 꺾여 손이 톡 튄다. 바탕이 없으면(가중치 0) 자연히 0 에서 올라간다.
        float baseRotationWeight = _animator.GetIKRotationWeight(AvatarIKGoal.RightHand);
        _animator.SetIKRotationWeight(
            AvatarIKGoal.RightHand, Mathf.Lerp(baseRotationWeight, rotationWeight, blend));

        // 값은 바탕이 실제로 깔려 있을 때만 섞는다. 가중치 0 일 때의 목표는 지난 프레임
        // 찌꺼기라 아무 자리도 가리키지 않는다.
        _animator.SetIKRotation(
            AvatarIKGoal.RightHand,
            baseRotationWeight > 0f
                ? Quaternion.Slerp(_animator.GetIKRotation(AvatarIKGoal.RightHand), targetHandRotation, blend)
                : targetHandRotation);
    }

    // 몸통 회전은 물리 틱에서 MoveRotation 으로 돌고 Rigidbody 보간까지 거쳐서, 렌더 프레임의
    // 카메라 yaw 보다 한 박자 늦다. 몸통 기준으로 조준 방향을 잡으면 화면을 돌리는 동안
    // 레이저가 카메라를 뒤따라오며 끌린다. 카메라 yaw 를 직접 기준으로 쓴다.
    private Vector3 CalculateAimDirection(out float pitch)
    {
        if (playerCameraController == null)
        {
            pitch = 0f;
            return transform.forward;
        }

        pitch = Mathf.Clamp(playerCameraController.ViewPitch, -maxAimPitch, maxAimPitch) * aimPitchWeight;

        Quaternion yaw = playerCameraController.ViewYawRotation;
        return Quaternion.AngleAxis(pitch, yaw * Vector3.right) * (yaw * Vector3.forward);
    }

    private void MoveHandTowardCenter(AvatarIKGoal hand, bool held)
    {
        bool isLeft = hand == AvatarIKGoal.LeftHand;
        Vector3 aimPosition;
        if (held)
        {
            Transform handTransform = _animator.GetBoneTransform(
                isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (handTransform == null)
            {
                return;
            }

            // 애니메이션의 손 위치에 중앙 보정과 카메라 상하 조준 높이를 적용한다.
            Vector3 localPosition = transform.InverseTransformPoint(handTransform.position);
            localPosition.x = Mathf.Lerp(localPosition.x, 0f, centerPull) + handSideOffset;
            localPosition.y += handRaise - currentAimPitch * handPitchOffsetPerDegree;
            localPosition.z += handForward;

            // 몸이 돌아가도 따라오도록 로컬로 얼려 둔다. 월드로 두면 놓고 도는 동안 손이 뒤에 남는다.
            if (isLeft)
            {
                frozenLeftHandPosition = localPosition;
            }
            else
            {
                frozenRightHandPosition = localPosition;
            }

            aimPosition = transform.TransformPoint(localPosition);
        }
        else
        {
            Vector3 startLocal = isLeft ? frozenLeftHandPosition : frozenRightHandPosition;
            if (!isLeft)
            {
                startLocal.x += returnStartSideOffset;
            }

            aimPosition = transform.TransformPoint(startLocal);
        }
        // 회전과 같은 방식이다. 가중치는 이어서 섞고, 값은 바탕이 있을 때만 섞는다.
        float baseWeight = _animator.GetIKPositionWeight(hand);
        _animator.SetIKPositionWeight(hand, Mathf.Lerp(baseWeight, positionWeight, blend));
        _animator.SetIKPosition(
            hand,
            baseWeight > 0f
                ? Vector3.Lerp(_animator.GetIKPosition(hand), aimPosition, blend)
                : aimPosition);
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
