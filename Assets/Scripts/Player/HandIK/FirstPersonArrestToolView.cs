using UnityEngine;

// 제압기를 1인칭에서는 뷰모델 총으로, 남의 화면에서는 지금까지처럼 캐릭터 손의 총으로 보여준다.
//
// 좌클릭하면 PlayerAimIK 가 진짜 팔을 화면 중앙으로 끌어오고 손에 붙은 제압기가 켜진다.
// 그 둘은 뷰모델과 자리도 크기도 달라서, 뷰모델에서 진짜 총으로 갈아 끼우면 반드시 툭 튄다.
// 그래서 내 화면에서는 갈아 끼우지 않는다. 뷰모델 총을 그대로 두고 진짜 총의 메시만 감춘다.
//
// 다만 레이저·총구 섬광은 진짜 총에 매달려 있어서, 그대로 두면 화면 밖 허리춤에서 터진다.
// 이펙트 뿌리를 뷰모델 총구 자리로 옮긴다. 부모는 바꾸지 않고 위치만 옮기는데,
// ArrestLaserBeam 이 transform.root 로 플레이어를 찾아 조준 방향과 자기 몸 제외를 판단하기
// 때문이다. 부모가 아이템으로 바뀌면 그 둘이 모두 깨져서 레이저가 엉뚱한 데로 나간다.
//
// 레이어도 건드리지 않는다. 오버레이 카메라는 far 가 5m 라, 거기로 옮기면 긴 레이저가 잘린다.
[DefaultExecutionOrder(120)]
public class FirstPersonArrestToolView : MonoBehaviour {
	private static readonly int IsUsingArrestToolHash = Animator.StringToHash("IsUsingArrestTool");

	[SerializeField] private PlayerCameraController _cameraController;
	[SerializeField] private PlayerItemIK _itemIK;
	[SerializeField] private Animator _animator;

	[Tooltip("Hand.R 밑의 AlienCaptureGun_Visual")]
	[SerializeField] private Transform _realTool;

	[Tooltip("이펙트가 모여 있는 뿌리. AlienCaptureGun_Visual/MuzzlePoint")]
	[SerializeField] private Transform _vfxRoot;

	private Renderer[] _realToolRenderers;
	private Vector3 _vfxHomePosition;
	private Quaternion _vfxHomeRotation;
	private bool _onViewmodel;

	private void Awake() {
		if (_cameraController == null || _itemIK == null || _realTool == null || _vfxRoot == null) {
			Debug.LogWarning("[FirstPersonArrestToolView] 참조가 비어 있어 꺼 둔다.", this);
			enabled = false;
			return;
		}

		// 파티클·레이저는 감추지 않는다. 감출 것은 총 몸통뿐이다.
		var meshes = new System.Collections.Generic.List<Renderer>();
		foreach (Renderer candidate in _realTool.GetComponentsInChildren<Renderer>(true)) {
			if (candidate is ParticleSystemRenderer || candidate is TrailRenderer || candidate is LineRenderer) {
				continue;
			}

			meshes.Add(candidate);
		}

		_realToolRenderers = meshes.ToArray();
		_vfxHomePosition = _vfxRoot.localPosition;
		_vfxHomeRotation = _vfxRoot.localRotation;
	}

	// 뷰모델 총이 이번 프레임 자리를 잡은 뒤에 따라붙어야 한 프레임 밀리지 않는다.
	private void LateUpdate() {
		// 진짜 총이 켜져 있는 동안에만 옮긴다. 꺼져 있으면 이펙트도 같이 꺼져 있어 옮길 것이 없다.
		ItemBase item = _itemIK.RightHandItem;
		bool useViewmodel = _realTool.gameObject.activeSelf
			&& _cameraController.IsFirstPersonHandsActive
			&& _animator != null
			&& _animator.GetBool(IsUsingArrestToolHash)
			&& item != null;

		if (useViewmodel != _onViewmodel) {
			_onViewmodel = useViewmodel;
			foreach (Renderer toolRenderer in _realToolRenderers) {
				toolRenderer.enabled = !useViewmodel;
			}

			// 되돌아갈 때는 원래 로컬 값을 한 번 복구해 두면 부모가 다시 자리를 잡아 준다.
			if (!useViewmodel) {
				_vfxRoot.SetLocalPositionAndRotation(_vfxHomePosition, _vfxHomeRotation);
			}
		}

		if (!useViewmodel) {
			return;
		}

		// 뷰모델 총은 같은 모델이라, 총 기준 로컬 값을 그대로 쓰면 총구 자리도 그대로다.
		Transform gun = item.transform;
		_vfxRoot.SetPositionAndRotation(
			gun.TransformPoint(_vfxHomePosition),
			gun.rotation * _vfxHomeRotation);
	}
}
