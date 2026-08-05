using UnityEngine;

public class BasicCart : CartBase {

	[Header("=== 잡았을 때의 스케일 비율 ===")]
	[SerializeField] private float _holdingScaleMultiplier = 0.7f;
	
	[Header("=== 카트 Transform 등록(잡았을 때 사이즈 줄이기 위해) ===")]
	[SerializeField] private MeshRenderer _cartRenderer;
	
	public override string CartName => "";
	
	private float _originalScale;

	protected override void Awake() {
		base.Awake();
		
		// 시작 시에 원래 스케일 저장
		_originalScale = _cartRenderer.transform.localScale.x;
	}

	// 기본 카트는 HolderId가 변경되었을 때 해야 할 추가적인 조치(내가 잡았을 땐 사이즈 줄이기)가 있어 override
	protected override void HandleHolderIdChanged(ulong oldId, ulong newId) {
		base.HandleHolderIdChanged(oldId, newId);
		
		// 내가 새로 잡았으면 스케일 작게 하기
		if (newId == NetworkManager.LocalClientId) {
			_cartRenderer.transform.localScale = Vector3.one * _originalScale * _holdingScaleMultiplier;
		}
		
		// 내가 잡고있다 놓았으면, 원래 스케일로 돌리기
		if (oldId == NetworkManager.LocalClientId) {
			_cartRenderer.transform.localScale = Vector3.one * _originalScale;
		}
	}
}