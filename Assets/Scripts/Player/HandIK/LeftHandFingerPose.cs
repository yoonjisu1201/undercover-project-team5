using UnityEngine;

// 손전등을 쥔 왼손 손가락을 웅크린 모양으로 고정한다.
//
// 손 위치는 IK 가 잡아 주지만 손가락은 애니메이션이 매 프레임 펴 버려서, 손전등을 쥐지 않고
// 편 손으로 받치고 있는 것처럼 보인다. 애니메이터가 본을 쓴 뒤인 LateUpdate 에서 다시 덮는다.
//
// 모양은 1인칭 뷰모델 손(Hand.L_FP)에서 그대로 가져온다. 같은 골격을 복제한 것이라 본 구성이
// 같고, 뷰모델 쪽에서 눈으로 맞춰 둔 자세를 3인칭에도 똑같이 쓸 수 있다.
public class LeftHandFingerPose : MonoBehaviour {
	private const string CopySuffix = "_FP";

	[Tooltip("자세를 가져올 뷰모델 손. Hand.L_FP")]
	[SerializeField] private Transform _sourceHand;

	[Tooltip("자세를 입힐 실제 손. Hand.L")]
	[SerializeField] private Transform _targetHand;

	// 손 IK 의 회전 가중치가 0.25 라, 목표를 돌려도 4분의 1만 반영돼 손목 각도를 잡을 수 없다.
	// 가중치를 올리면 왼팔 자세가 통째로 바뀌므로, 손가락과 같은 방식으로 애니메이터 뒤에서
	// 손목 본만 따로 꺾는다. 애니메이션 자세는 그대로 두고 각도만 얹는 셈이다.
	[Tooltip("정면을 볼 때 손목을 꺾는 각도(도). 양수면 손이 아래를 향한다")]
	[Range(-90f, 90f)]
	[SerializeField] private float _wristPitch = 25f;

	// 손목을 고정해 두면 위를 올려다볼 때 팔은 올라가는데 손목만 계속 숙이고 있어서 어색하다.
	// 시야를 따라 같이 펴지게 한다. 내려다볼 때는 더 숙는다.
	[Tooltip("올려다볼 때 손목이 펴지는 비율. 1 이면 시야와 같은 각도만큼 펴진다")]
	[Range(0f, 1.5f)]
	[SerializeField] private float _wristFollowViewRatio = 0.8f;

	// 올려다볼 때와 같은 비율로 내리면 발밑을 볼 때 손목이 꺾일 수 있는 한계까지 접힌다.
	// 이미 기본값부터 숙인 자세라 내려가는 쪽은 조금만 더한다.
	[Tooltip("내려다볼 때 손목이 더 숙는 비율. 올려다볼 때보다 작게 둔다")]
	[Range(0f, 1f)]
	[SerializeField] private float _wristFollowDownRatio = 0.2f;

	// 팔은 원래 ArmFollowPivot 이 IK 목표를 끌어올려 들게 되어 있는데, 왼손 IK 가 실제로는
	// 안 걸려 있어서(목표와 손이 46cm 벌어져 있다) 피벗을 돌려도 팔이 그대로 있다.
	// 손목과 같은 방식으로 어깨를 직접 돌려서 팔 전체를 든다.
	[Tooltip("올려다볼 때 어깨를 돌려 팔 전체를 드는 비율. 1 이면 시야와 같은 각도만큼 든다")]
	[Range(0f, 2f)]
	[SerializeField] private float _armLiftViewRatio = 1f;

	[Tooltip("팔이 올라갈 수 있는 최대 각도(도)")]
	[Range(0f, 90f)]
	[SerializeField] private float _maxArmLift = 60f;

	[Tooltip("0 이면 애니메이션 그대로, 1 이면 완전히 웅크린 모양")]
	[Range(0f, 1f)]
	[SerializeField] private float _weight = 1f;

	// 뷰모델 손보다 더 쥐고 싶을 때 쓴다. 각 마디를 원래 굽은 축 그대로 더 굽히므로,
	// 손가락마다 굽는 방향을 따로 지정할 필요가 없다.
	[Tooltip("뷰모델 손보다 더 쥐는 정도(도). 0 이면 뷰모델과 같은 모양")]
	[Range(0f, 45f)]
	[SerializeField] private float _extraCurl;

	private PlayerCameraController _cameraController;
	private Transform _upperArm;

	// 본 회전을 지금 값에 곱해서 얹는데, 이건 애니메이터가 매 프레임 본을 새로 써 준다는
	// 전제 위에서만 안전하다. 컬링이나 Animator 비활성으로 그 갱신이 한 번이라도 빠지면
	// 우리가 쓴 값 위에 또 얹혀 프레임마다 각도가 쌓이고 팔이 돌아가 버린다.
	// 직전에 쓴 값과 지금 값이 같으면 애니메이터가 안 썼다는 뜻이라, 그때는 우리가 얹기 전
	// 자세에서 다시 계산한다.
	private Quaternion _upperArmSource;
	private Quaternion _upperArmWritten;
	private Quaternion _handSource;
	private Quaternion _handWritten;
	private bool _hasWritten;

	private Transform[] _bones;
	private Quaternion[] _rotations;
	private Vector3[] _curlAxes;

	private void Awake() {
		_cameraController = GetComponent<PlayerCameraController>();

		Animator animator = GetComponent<Animator>();
		if (animator != null) {
			_upperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
		}

		// 손목·어깨는 실제 손만 있으면 된다. 이게 없을 때만 통째로 끈다.
		if (_targetHand == null) {
			Debug.LogWarning("[LeftHandFingerPose] 실제 손(Hand.L) 참조가 비어 있어 꺼 둔다.", this);
			enabled = false;
			return;
		}

		// 아래는 손가락 자세 전용이다. 못 갖추더라도 손목·어깨는 계속 돌아야 하므로
		// 컴포넌트를 끄지 않고 손가락만 건너뛴다.
		_bones = System.Array.Empty<Transform>();
		_rotations = System.Array.Empty<Quaternion>();
		_curlAxes = System.Array.Empty<Vector3>();

		if (_sourceHand == null) {
			Debug.LogWarning("[LeftHandFingerPose] 뷰모델 손 참조가 없어 손가락 자세만 건너뛴다.", this);
			return;
		}

		var bones = new System.Collections.Generic.List<Transform>();
		var rotations = new System.Collections.Generic.List<Quaternion>();
		var curlAxes = new System.Collections.Generic.List<Vector3>();

		// 뷰모델 손 밑에는 손전등 앵커처럼 본이 아닌 것도 붙어 있다. 접미사로 본만 골라낸다.
		foreach (Transform source in _sourceHand.GetComponentsInChildren<Transform>(true)) {
			if (source == _sourceHand || !source.name.EndsWith(CopySuffix)) {
				continue;
			}

			string boneName = source.name.Substring(0, source.name.Length - CopySuffix.Length);
			Transform target = FindBone(_targetHand, boneName);
			if (target == null) {
				continue;
			}

			bones.Add(target);
			// 뷰모델 손은 애니메이터가 건드리지 않아 자세가 고정이다. 한 번만 읽어 둔다.
			rotations.Add(source.localRotation);

			// 마디가 이미 굽어 있는 축. 같은 축으로 더 돌리면 자연스럽게 더 쥔 모양이 된다.
			source.localRotation.ToAngleAxis(out float angle, out Vector3 axis);
			curlAxes.Add(angle > 0.01f ? axis : Vector3.right);
		}

		_bones = bones.ToArray();
		_rotations = rotations.ToArray();
		_curlAxes = curlAxes.ToArray();

		if (_bones.Length == 0) {
			Debug.LogWarning("[LeftHandFingerPose] 이름이 맞는 손가락 본을 못 찾아 손가락 자세만 건너뛴다.", this);
		}
	}

	private static Transform FindBone(Transform root, string boneName) {
		foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) {
			if (child.name == boneName) {
				return child;
			}
		}

		return null;
	}

	private void LateUpdate() {
		// 위를 보면 ViewPitch 가 음수라 꺾는 각도가 줄고, 그만큼 손목이 펴져 올라간다.
		float viewPitch = _cameraController != null ? _cameraController.ViewPitch : 0f;

		// 어깨부터 든다. 손목보다 먼저 해야 손목 각도가 들린 팔 위에 얹힌다.
		// 팔은 아래로 늘어져 있어서, 오른쪽 축 음의 회전이 팔을 앞으로 들어 올린다.
		if (_upperArm != null) {
			Quaternion source = _hasWritten && _upperArm.rotation == _upperArmWritten
				? _upperArmSource
				: _upperArm.rotation;
			_upperArmSource = source;

			float lift = viewPitch < 0f
				? Mathf.Min(-viewPitch * _armLiftViewRatio, _maxArmLift)
				: 0f;
			_upperArmWritten = Quaternion.AngleAxis(-lift, transform.right) * source;
			_upperArm.rotation = _upperArmWritten;
		}

		float wristAngle = _wristPitch + (viewPitch < 0f
			? viewPitch * _wristFollowViewRatio
			: viewPitch * _wristFollowDownRatio);

		wristAngle = Mathf.Clamp(wristAngle, -90f, 90f);

		// 손전등은 이 손을 따라오므로(실행 순서가 뒤다) 손목을 꺾으면 손전등도 같이 숙는다.
		Quaternion handSource = _hasWritten && _targetHand.rotation == _handWritten
			? _handSource
			: _targetHand.rotation;
		_handSource = handSource;
		_handWritten = Quaternion.AngleAxis(wristAngle, transform.right) * handSource;
		_targetHand.rotation = _handWritten;

		_hasWritten = true;

		if (_weight <= 0f) {
			return;
		}

		for (int i = 0; i < _bones.Length; i++) {
			Quaternion pose = _extraCurl > 0f
				? Quaternion.AngleAxis(_extraCurl, _curlAxes[i]) * _rotations[i]
				: _rotations[i];

			_bones[i].localRotation = _weight >= 1f
				? pose
				: Quaternion.Slerp(_bones[i].localRotation, pose, _weight);
		}
	}
}
