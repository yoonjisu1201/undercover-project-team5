using UnityEngine;

// 1인칭 뷰모델 손을 시점·점프·걸음에 맞춰 움직인다.
//
// 뷰모델 손은 카메라의 자식이라 그대로 두면 화면에 완전히 고정된다. 위를 봐도, 뛰어올라도,
// 달려도 손은 화면 아래 같은 자리에 붙어 있어서 몸과 따로 논다.
//
// 네 가지를 얹는다.
//  - 시점: 올려다보면 왼손이 따라 올라간다. 3인칭 ArmFollowPivot 이 하는 일과 같은 방향이다.
//          화면 앞에서 3인칭과 같은 양(도당 0.53cm)을 움직이면 손이 화면을 벗어나 양은 따로 잡는다.
//  - 점프: 양손이 팔꿈치를 축으로 돌아 올라갔다 내려온다. 오르내리는 길이를 실제 체공에 맞춰
//          두어서, 팔이 내려앉는 시점과 발이 닿는 시점이 겹친다.
//  - 걸음: 양손이 8자로 흔들린다. 달리면 더 크고 빠르게.
//  - 오른손: 빈손이면 화면 아래로 내려 감춘다. 감춘 자리가 곧 기본 자리라, 점프 아크가 그대로
//            손을 화면 안으로 끌어올렸다가 다시 내려보낸다.
[DefaultExecutionOrder(60)]
public class FirstPersonHandMotion : MonoBehaviour {
	private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
	private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");

	// 외계 제압기를 들면 PlayerAimIK 가 진짜 팔을 화면 중앙으로 끌어와 총을 조준한다.
	// 그때 뷰모델 손까지 떠 있으면 손이 두 쌍 보인다.
	private static readonly int IsUsingArrestToolHash = Animator.StringToHash("IsUsingArrestTool");

	[SerializeField] private PlayerCameraController _cameraController;
	[SerializeField] private PlayerMoveSample _playerMove;
	[SerializeField] private PlayerItemIK _itemIK;

	// 걷기·달리기 상태는 애니메이터가 이미 들고 있고 모든 피어에 동기화된다.
	// PlayerMoveSample 에 접근자를 새로 뚫지 않고 그대로 읽어 쓴다.
	[SerializeField] private Animator _animator;

	[Tooltip("오버레이 카메라 밑의 LeftHandRig")]
	[SerializeField] private Transform _leftHandRig;

	[Tooltip("오버레이 카메라 밑의 RightHandRig")]
	[SerializeField] private Transform _rightHandRig;


	[Header("=== 시점 (왼손만) ===")]
	// 손을 그냥 위로 밀어 올리면 팔이 늘어난 것처럼 보인다. 사람 팔은 팔꿈치를 축으로 돌아서
	// 손이 호를 그리며 올라가고 손목 각도도 같이 바뀐다. 그래서 이동이 아니라 회전으로 올린다.
	[Tooltip("손이 도는 축의 위치. 손 기준 오프셋이라 뒤쪽 아래(팔꿈치 자리)를 넣는다")]
	[SerializeField] private Vector3 _pitchPivotOffset = new Vector3(-0.06f, -0.15f, -0.26f);

	[Tooltip("올려다볼 때 시야각 1도당 팔이 도는 각도. 0.5 면 50도 올려다볼 때 팔이 25도 돈다")]
	[SerializeField] private float _pitchUpRatio = 0.5f;

	// 내려다볼 때 손이 같이 내려가면 화면 밖으로 빠져 버린다. 발밑을 볼 때야말로 손이 보여야 해서,
	// 위아래 어느 쪽을 보든 손은 올라오게 한다. 다만 올려다볼 때보다는 덜 올린다.
	[Tooltip("내려다볼 때 시야각 1도당 팔이 도는 각도. 이쪽도 손이 올라간다")]
	[SerializeField] private float _pitchDownRatio = 0.25f;

	[Tooltip("아무리 올려다봐도 이 이상은 안 돈다(도)")]
	[SerializeField] private float _maxPitchAngle = 30f;

	[Header("=== 점프 (양손) ===")]
	// 양손 모두 위로 밀어 올리지 않고 팔꿈치를 축으로 돌린다. 시점을 따라 올라갈 때와 같은
	// 방식이라 손목 각도까지 같이 따라와서 훨씬 깔끔하다.
	[Tooltip("점프 중 오른손이 팔꿈치를 축으로 도는 각도(도)")]
	[SerializeField] private float _jumpRightDegrees = 28f;

	[Tooltip("오른손이 도는 축의 위치. 왼손 축을 좌우 대칭시킨 값")]
	[SerializeField] private Vector3 _rightPivotOffset = new Vector3(0.06f, -0.15f, -0.26f);

	// 팔은 올릴 때보다 내릴 때가 빠르다. 올라갈 때는 근육으로 버티며 올리고 내려올 때는
	// 힘을 빼서 떨어지기 때문이다. 같은 시간을 주면 둘 다 굼떠 보인다.
	[Tooltip("올라가는 데 걸리는 시간(초)")]
	[SerializeField] private float _jumpRiseDuration = 0.34f;

	[Tooltip("내려오는 데 걸리는 시간(초). 올라가는 시간보다 짧게 둔다")]
	[SerializeField] private float _jumpFallDuration = 0.31f;

	// 위아래로만 움직이면 손이 승강기처럼 오르내린다. 앞뒤를 한 주기 더 얹으면 옆에서 볼 때
	// 손이 고리를 그리며 올라갔다 내려온다.
	[Tooltip("점프 중 오른손이 앞뒤로 흔들리는 거리(m). 올라갈 때 앞으로, 내려올 때 뒤로")]
	[SerializeField] private float _jumpSwing = 0.05f;

	// 왼손은 손전등을 쥐고 있어 늘 보인다. 오른손처럼 고리를 그리면 화면 앞에서 요란하다.
	// 시점을 따라 올라갈 때와 같은 방식으로, 팔꿈치를 축으로 돌리기만 한다.
	[Tooltip("점프 중 왼손이 팔꿈치를 축으로 도는 각도(도)")]
	[SerializeField] private float _jumpLeftDegrees = 12f;

	[Header("=== 제압기 들어올리기 ===")]
	// 좌클릭과 동시에 뷰모델을 접으면 총이 손에서 사라졌다가 중앙에 툭 나타난다.
	// 뷰모델 오른손을 화면 중앙으로 올리고, 다 올라간 뒤에 진짜 팔로 넘긴다.
	[Tooltip("오른손이 중앙으로 올라가는 데 걸리는 시간(초). PlayerArrestInput 의 지연과 맞춘다")]
	[SerializeField] private float _arrestRaiseDuration = 0.22f;

	// z 를 기본 자세(0.36 쯤)보다 작게 둬야 손이 몸 쪽으로 당겨진다. 크게 두면 앞으로 뻗는다.
	[Tooltip("다 올라갔을 때 오른손 자리 (카메라 기준). 중앙 + 몸 쪽으로 당김")]
	[SerializeField] private Vector3 _arrestRaisePosition = new Vector3(0.0570f, -0.3062f, 0.2342f);

	[Tooltip("다 올라갔을 때 오른손 회전 (기본 자세 기준 추가 회전)")]
	[SerializeField] private Vector3 _arrestRaiseEuler = new Vector3(20.21f, 1.92f, 323.95f);

	// 왼손은 손전등을 쥔 채로 같이 모인다. 오른손만 올라가면 손전등이 화면 구석에 남는다.
	[Tooltip("다 올라갔을 때 총이 커지는 배수. 손 크기는 그대로 두고 총만 키운다")]
	[SerializeField] private float _arrestGunScale = 1.88f;


	[Tooltip("다 올라갔을 때 왼손 자리 (카메라 기준)")]
	[SerializeField] private Vector3 _arrestRaiseLeftPosition = new Vector3(-0.1691f, -0.5972f, 0.3960f);

	// 손전등 배럴이 총구와 같은 방향을 보도록 맞춘 값. 오른손 회전(_arrestRaiseEuler)이나
	// 총의 HoldRotationOffset 을 바꾸면 총구 방향이 달라지므로 이 값도 다시 잡아야 한다.
	[Tooltip("다 올라갔을 때 왼손 회전 (기본 자세 기준 추가 회전). 손전등이 총구 방향을 보게 맞춘 값")]
	[SerializeField] private Vector3 _arrestRaiseLeftEuler = new Vector3(330.91f, 0.55f, 217.16f);

	[Header("=== 오른손 감추기 ===")]
	// 빈손이면 감추되 그 자리에서 사라지면 눈에 띈다. 화면 아래로 내려보낸 뒤에 끈다.
	[Tooltip("감출 때 손이 내려가는 거리(m)")]
	[SerializeField] private Vector3 _rightStowOffset = new Vector3(0f, -0.35f, 0f);

	[Tooltip("내려가고 올라오는 속도. 클수록 빠르다")]
	[SerializeField] private float _stowSpeed = 3f;

	[Header("=== 걷기·달리기 흔들림 (양손) ===")]
	[SerializeField] private float _walkBobAmount = 0.005f;
	[SerializeField] private float _walkBobSpeed = 14f;
	[SerializeField] private float _runBobAmount = 0.009f;
	[SerializeField] private float _runBobSpeed = 18f;

	[Tooltip("멈추고 걸을 때 흔들림이 붙고 빠지는 속도")]
	[SerializeField] private float _bobBlendSpeed = 0.08f;

	// 0 이면 두 손이 같이 움직이고(정박), 180 이면 반대로 움직인다(엇박).
	[Tooltip("오른손 흔들림의 위상 차이(도). 0 이면 왼손과 같이, 180 이면 엇박")]
	[SerializeField] private float _rightBobPhaseOffset;

	private Vector3 _leftBase;
	private Quaternion _leftBaseRotation;
	private Vector3 _rightBase;
	private Quaternion _rightBaseRotation;
	private float _bobAmount;
	private float _bobPhase;

	private enum JumpState { None, Rising, Falling }

	private JumpState _jumpState = JumpState.None;
	private float _jumpTimer;
	private float _jumpLift01;
	private float _jumpPhase;
	private float _fallFromLift;
	private float _fallFromPhase;
	private bool _wasJumping;
	// 빈손으로 시작하는 게 보통이다. 0 으로 두면 첫 프레임에 오른손이 잠깐 보였다 사라진다.
	private float _stowBlend = 1f;
	private bool _rightHandActive = true;
	private float _arrestRaise;


	private void Awake() {
		if (_cameraController == null || _leftHandRig == null || _rightHandRig == null) {
			Debug.LogWarning("[FirstPersonHandMotion] 참조가 비어 있어 꺼 둔다.", this);
			enabled = false;
			return;
		}

		_leftBase = _leftHandRig.localPosition;
		_leftBaseRotation = _leftHandRig.localRotation;
		_rightBase = _rightHandRig.localPosition;
		_rightBaseRotation = _rightHandRig.localRotation;
	}

	// 카메라가 이번 프레임 자세를 잡은 뒤에 손을 옮겨야 한 프레임 밀리지 않는다.
	private void LateUpdate() {
		// 뷰모델은 내 화면에만 있다. 다른 클라이언트에서도 이 계산이 돌면, 보이지도 않는 손을
		// 움직이는 것은 물론이고 손에 든 아이템 크기까지 같이 바뀌어 남의 화면에서 총이
		// 커졌다 작아진다. 오버레이가 꺼져 있으면(논오너·다운 시점) 아무것도 하지 않는다.
		if (!_cameraController.IsFirstPersonHandsActive) {
			if (_itemIK != null) {
				_itemIK.SetRightHandItemScale(1f);
			}

			return;
		}

		// 좌클릭하면 오른손이 화면 중앙으로 올라가고, 다 올라간 다음에야 진짜 팔로 넘어간다.
		bool raisingArrestTool = _animator != null && _animator.GetBool(IsUsingArrestToolHash);
		float raiseStep = _arrestRaiseDuration > 0f ? Time.deltaTime / _arrestRaiseDuration : 1f;
		_arrestRaise = Mathf.MoveTowards(_arrestRaise, raisingArrestTool ? 1f : 0f, raiseStep);


		// 손을 가까이 당기는 대신 총만 키운다. 손까지 커지면 화면을 다 덮는다.
		if (_itemIK != null) {
			_itemIK.SetRightHandItemScale(
				Mathf.Lerp(1f, _arrestGunScale, Mathf.SmoothStep(0f, 1f, _arrestRaise)));
		}

		ResolveJumpArc(out float jumpLift01, out float jumpSwing);

		// 흔들기는 발이 닿을 때까지 멈춘다. 공중에서 켜 두면 걷는 손이 되어 툭 끊긴다.
		bool airborne = _playerMove != null && _playerMove.IsJumping;

		UpdateBob(airborne);
		Vector3 bobLeft = BobAt(0f);
		Vector3 bobRight = BobAt(_rightBobPhaseOffset * Mathf.Deg2Rad);

		// 위를 보면 pitch 가 음수다. 축이 손보다 뒤에 있어서 X 축 음의 회전이 손을 들어 올린다.
		// 내려다볼 때는 pitch 가 양수라, 부호를 뒤집어야 그쪽에서도 손이 올라온다.
		float viewPitch = _cameraController.ViewPitch;
		float ratio = viewPitch < 0f ? _pitchUpRatio : -_pitchDownRatio;
		float pitchAngle = Mathf.Clamp(viewPitch * ratio, -_maxPitchAngle, _maxPitchAngle);

		// 점프도 같은 축으로 돌려서 올린다. 음의 회전이 손을 들어 올린다.
		float leftAngle = pitchAngle - jumpLift01 * _jumpLeftDegrees;
		Quaternion leftRotation = Quaternion.AngleAxis(leftAngle, Vector3.right);

		// 팔꿈치 자리를 축으로 돌린다. 손이 호를 그리며 올라가고 손목 각도도 같이 따라온다.
		Vector3 pivot = _leftBase + _pitchPivotOffset;
		Vector3 leftPosition = pivot + leftRotation * (_leftBase - pivot) + bobLeft;
		Quaternion leftFinal = leftRotation * _leftBaseRotation;

		// 제압기를 올릴 때는 왼손도 손전등을 쥔 채 같이 중앙으로 모인다.
		if (_arrestRaise > 0f) {
			float raiseLeft = Mathf.SmoothStep(0f, 1f, _arrestRaise);
			leftPosition = Vector3.Lerp(leftPosition, _arrestRaiseLeftPosition, raiseLeft);
			leftFinal = Quaternion.Slerp(
				leftFinal, Quaternion.Euler(_arrestRaiseLeftEuler) * _leftBaseRotation, raiseLeft);
		}

		_leftHandRig.localPosition = leftPosition;
		_leftHandRig.localRotation = leftFinal;

		// 빈손이면 감춘 자리가 기본 자리다. 점프 아크가 그대로 손을 화면 안으로 끌어올렸다가
		// 다시 내려보낸다. 아크와 감추기를 따로 돌리면 손이 원래 자리에서 한 번 멈췄다가
		// 뒤늦게 화면 밖으로 내려가서 끊겨 보인다.
		// 제압기를 쓰는 동안에는 계속 보여야 한다. 1인칭에서는 진짜 팔로 갈아 끼우지 않고
		// 뷰모델 총을 끝까지 쓰기 때문이다.
		bool hasItem = _itemIK != null && _itemIK.HasRightHandItem;
		float stowTarget = hasItem || _arrestRaise > 0f ? 0f : 1f - jumpLift01;

		// 아크가 도는 동안에는 그 곡선을 그대로 따른다. 이미 부드러워서 더 뭉갤 필요가 없다.
		// 아이템을 들거나 놓아 목표가 툭 바뀔 때만 서서히 옮긴다.
		_stowBlend = _jumpState != JumpState.None
			? stowTarget
			: Mathf.MoveTowards(_stowBlend, stowTarget, _stowSpeed * Time.deltaTime);

		// 화면 아래로 완전히 내려간 뒤에 끈다. 제자리에서 사라지면 툭 꺼지는 게 보인다.
		Vector3 stow = _rightStowOffset * Mathf.SmoothStep(0f, 1f, _stowBlend);
		Quaternion rightRotation = Quaternion.AngleAxis(-jumpLift01 * _jumpRightDegrees, Vector3.right);
		Vector3 rightPivot = _rightBase + _rightPivotOffset;
		Vector3 rightPosition = rightPivot + rightRotation * (_rightBase - rightPivot)
			+ bobRight + new Vector3(0f, 0f, jumpSwing) + stow;
		Quaternion rightFinal = rightRotation * _rightBaseRotation;

		// 올라가는 동안에는 중앙 자세로 섞는다.
		if (_arrestRaise > 0f) {
			float raise = Mathf.SmoothStep(0f, 1f, _arrestRaise);
			rightPosition = Vector3.Lerp(rightPosition, _arrestRaisePosition, raise);
			rightFinal = Quaternion.Slerp(
				rightFinal, Quaternion.Euler(_arrestRaiseEuler) * _rightBaseRotation, raise);
		}

		_rightHandRig.localPosition = rightPosition;
		_rightHandRig.localRotation = rightFinal;

		bool active = _stowBlend < 1f;
		if (active != _rightHandActive) {
			_rightHandActive = active;
			_rightHandRig.gameObject.SetActive(active);
		}
	}

	// 뛰어오르면 팔을 올리고 정점에서 곧바로 내린다. 오르내리는 길이를 실제 체공(상승 0.34초 /
	// 낙하 0.31초)에 맞춰 두어서, 팔이 내려앉는 시점과 발이 닿는 시점이 대략 겹친다.
	// 그전에 착지하면 올라가던 높이에서 이어서 내린다.
	//
	// lift01 은 0~1 로 올라간 정도. 양손 모두 각자의 팔꿈치 축 회전량으로 쓴다.
	// phase 는 앞뒤 흔들림용이다. 올라가며 0→0.5, 내려오며 0.5→1 로 가서 고리를 한 바퀴 그린다.
	private void ResolveJumpArc(out float lift01, out float swing) {
		bool airborne = _playerMove != null && _playerMove.IsJumping;

		if (airborne && !_wasJumping) {
			_jumpState = JumpState.Rising;
			_jumpTimer = 0f;
		}
		else if (!airborne && _wasJumping && _jumpState == JumpState.Rising) {
			// 아직 올라가는 중에 발이 닿았다(짧은 점프). 그 높이에서 곧바로 이어서 내린다.
			//
			// 이미 내려오는 중이라면 건드리지 않는다. 여기서 다시 시작하면 남은 몇 cm 를 낙하
			// 시간 전체에 걸쳐 내리게 되어 손이 멈춘 것처럼 보이다가 사라진다.
			_fallFromLift = _jumpLift01;
			_fallFromPhase = _jumpPhase;
			_jumpState = JumpState.Falling;
			_jumpTimer = 0f;
		}

		_wasJumping = airborne;

		switch (_jumpState) {
			case JumpState.Rising: {
				_jumpTimer += Time.deltaTime;
				float u = Mathf.Clamp01(_jumpTimer / _jumpRiseDuration);
				// 시작에서 확 치고 올라가 정점에서 잦아든다.
				_jumpLift01 = Mathf.Sin(Mathf.PI * 0.5f * u);
				_jumpPhase = 0.5f * u;
				// 정점에서 멈추지 않고 곧바로 떨어진다. 올라간 팔이 공중에 걸려 있으면 어색하다.
				if (u >= 1f) {
					_fallFromLift = 1f;
					_fallFromPhase = 0.5f;
					_jumpState = JumpState.Falling;
					_jumpTimer = 0f;
				}

				break;
			}

			case JumpState.Falling: {
				_jumpTimer += Time.deltaTime;
				float v = Mathf.Clamp01(_jumpTimer / _jumpFallDuration);
				// SmoothStep 은 끝에서 속도가 0 이라 원래 자리에 부드럽게 안착한다.
				_jumpLift01 = _fallFromLift * (1f - Mathf.SmoothStep(0f, 1f, v));
				_jumpPhase = Mathf.Lerp(_fallFromPhase, 1f, v);
				if (v >= 1f) {
					_jumpLift01 = 0f;

					// 아직 공중이면(높은 데서 뛰어내림) 제자리에 둔 채 기다린다. 여기서 끝내 버리면
					// 오른손이 공중에서 화면 밖으로 사라졌다가 착지할 때 다시 나타난다.
					if (!airborne) {
						_jumpState = JumpState.None;
					}
				}

				break;
			}
		}

		lift01 = _jumpLift01;
		swing = _jumpState == JumpState.None ? 0f : Mathf.Sin(2f * Mathf.PI * _jumpPhase) * _jumpSwing;
	}

	// 발이 땅에 있을 때만 흔든다. 공중에서까지 흔들면 걷는 것처럼 보인다.
	private void UpdateBob(bool jumping) {
		bool running = false;
		bool moving = false;
		if (_animator != null && !jumping) {
			moving = _animator.GetBool(IsMovingHash);
			running = _animator.GetBool(IsRunningHash);
		}

		float targetAmount = moving ? (running ? _runBobAmount : _walkBobAmount) : 0f;
		_bobAmount = Mathf.MoveTowards(_bobAmount, targetAmount, _bobBlendSpeed * Time.deltaTime);

		if (moving) {
			_bobPhase += (running ? _runBobSpeed : _walkBobSpeed) * Time.deltaTime;
		}
	}

	// 위아래가 주기이고 좌우는 절반 주기로 따라붙어 8자를 그린다. 위아래만 흔들면 기계처럼 보인다.
	private Vector3 BobAt(float phaseOffset) {
		float phase = _bobPhase + phaseOffset;
		return new Vector3(
			Mathf.Cos(phase * 0.5f) * _bobAmount * 0.6f,
			Mathf.Sin(phase) * _bobAmount,
			0f);
	}
}
