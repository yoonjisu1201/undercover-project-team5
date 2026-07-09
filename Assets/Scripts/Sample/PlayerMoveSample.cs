using UnityEngine;


/* InputActions를 활용하여 Input을 처리하는 방법 샘플입니다.
 * 현재 /Asset/CustomInputActions 파일 활용하고 있습니다.
 * 추가 액션을 넣고 싶다면 위 경로 파일 내부 내용을 수정하면 됩니다.
 */

public class PlayerMoveSample : MonoBehaviour {
	[SerializeField] private float _moveSpeed = 5f;
	[SerializeField] private float _rotateSpeed = 0.5f;
	
	private float _yaw = 0f;
	private float _pitch = 0f;
	
	// 만들어 둔 InputActions 파일
	CustomInputActions _actions;
	
	private void Awake() {
		// Awake에서 새로 생성
		_actions = new CustomInputActions();
		_actions.Enable();
	}

	private void Update() {
		/// 이동 방식 적용하기
		// Player - Move에서 값을 Vector2 타입으로 읽어오기
		Vector2 move = _actions.Player.Move.ReadValue<Vector2>();
		
		// forward, right 방향 설정
		Vector3 forward = transform.forward;
		Vector3 right = transform.right;
		
		// forward, right 방향에서 y값은 제거 (위쪽 보고 있다고 위로 날아가는 상황 방지)
		forward.y = 0;
		right.y = 0;
		
		// 이후 값을 1에 맞춰주기 위한 Normalize
		forward.Normalize();
		right.Normalize();
		
		// 이동에 적용
		transform.position += forward * move.y * _moveSpeed * Time.deltaTime;
		transform.position += right * move.x * _moveSpeed * Time.deltaTime;
		
		/// 마우스 관련 이동 적용하기
		Vector2 mouseDelta = _actions.Player.Mouse.ReadValue<Vector2>();
		
		// 현재 yaw, pitch에 값 적용
		_yaw += mouseDelta.x * _rotateSpeed;
		_pitch -= mouseDelta.y * _rotateSpeed;
		// 위로 쭉 민다고 시야 뒤로 넘어가지 않게 -80 ~ 80사이 값으로 유지
		_pitch = Mathf.Clamp(_pitch, -80f, 80f);
		
		// 회전 적용
		transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
		
		/// 버튼 입력 방식 적용하기
		// Player - Interact라는 행동이 이번 프레임에 눌렸는지 확인한다.
		// Keyboard.current.eKey.wasPressedThisFrame와 비슷하게 동작함
		if (_actions.Player.Interact.WasPressedThisFrame()) {
			Debug.Log($"상호작용 키 눌림!");
		}
	}
}
