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

	[Tooltip("0 이면 애니메이션 그대로, 1 이면 완전히 웅크린 모양")]
	[Range(0f, 1f)]
	[SerializeField] private float _weight = 1f;

	// 뷰모델 손보다 더 쥐고 싶을 때 쓴다. 각 마디를 원래 굽은 축 그대로 더 굽히므로,
	// 손가락마다 굽는 방향을 따로 지정할 필요가 없다.
	[Tooltip("뷰모델 손보다 더 쥐는 정도(도). 0 이면 뷰모델과 같은 모양")]
	[Range(0f, 45f)]
	[SerializeField] private float _extraCurl;

	private Transform[] _bones;
	private Quaternion[] _rotations;
	private Vector3[] _curlAxes;

	private void Awake() {
		if (_sourceHand == null || _targetHand == null) {
			Debug.LogWarning("[LeftHandFingerPose] 손 참조가 비어 있어 꺼 둔다.", this);
			enabled = false;
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
			Debug.LogWarning("[LeftHandFingerPose] 이름이 맞는 손가락 본을 못 찾아 꺼 둔다.", this);
			enabled = false;
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
