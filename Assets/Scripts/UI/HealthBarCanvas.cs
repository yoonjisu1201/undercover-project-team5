using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 체력바 등의 구현에 사용됨. 오른쪽에서 왼쪽으로 점점 이미지가 사라지는 모습 구현을 위함
// Image.fillAmount로 하면 체력바가 깨져 보여서, 대신 Mask로 자르는 방식을 사용함
public class HealthBarCanvas : NetworkBehaviour {
	[Header("=== 필요한 오브젝트 등록 ===")]
	[SerializeField] private Image _healthBar; 
	[SerializeField] private Image _barMask;

	[Header("=== 플레이어와 얼마나 가까워야 보이게 할 지 ===")] 
	[SerializeField] private float _showDistance = 20f;
	
	private float _maxWidth;
	private Camera _mainCamera;
	private Player _player;

	// 네트워크 스폰 시에 플레이어 저장
	public override void OnNetworkSpawn() {
		_player = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Player>();
		_mainCamera = LocalCameraProvider.MainCamera;
	}

	private void Awake() {
		// 오른쪽 -> 왼쪽으로 없애기 위해 Anchor, Pivot 조절
		_barMask.rectTransform.anchoredPosition = new Vector2(0f, 0.5f);
		_barMask.rectTransform.pivot = new Vector2(0f, 0.5f);
		
		// 비율 조절을 위해 크기 저장해두기
		_maxWidth = _barMask.rectTransform.rect.width;
	}
	
	public void SetBarFillAmount(float percent) {
		// 체력 비율을 0 ~ 1 사이로 클램핑
		percent = Mathf.Clamp(percent, 0f, 1f);
		
		// 클램핑된 결과 기반으로 얼마나 채울지(width를 얼마로 할지) 결정
		Vector2 newSize = _barMask.rectTransform.sizeDelta;
		newSize.x = percent * _maxWidth;
		
		// 적용
		_barMask.rectTransform.sizeDelta = newSize;
	}

	private void Update() {
		// 플레이어와의 거리에 따라 체력바 활성화, 비활성화
		_healthBar.gameObject.SetActive(
			Vector3.SqrMagnitude(transform.position - _player.transform.position) <= _showDistance * _showDistance
		);
	}
	
	private void LateUpdate() {
		// 체력바는 항상 메인 카메라와 같은 방향을 보도록. 근데 x회전은 0을 유지하고자 함
		Quaternion targetRotation = _mainCamera.transform.rotation;
		targetRotation.x = 0f;
		transform.rotation = targetRotation; 
	}
}