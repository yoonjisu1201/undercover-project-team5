using TMPro;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PlayerInfoPresenter : MonoBehaviour {
	[Header("=== 미니맵에 표시될 플레이어 이름 ===")]
	[SerializeField] private TMP_Text _minimapNameText;

	[Header("=== 미니맵 마커 ===")] 
	[SerializeField] private Image _minimapMarker;

    [Header("=== 미니맵 방향 화살표 ===")]
	[SerializeField] private Image _minimapDirectionArrow;

    [Header("=== 캐릭터의 머리 위에 뜨는 닉네임마커 ===")] 
	[SerializeField] private PlayerNameTag _nameTag;
	
	private Player _player;

	// 플레이어 카메라에서는 미니맵 마커, 미니맵 이름표 안 보이게 만들어주기
	private void Awake() {
		_minimapNameText.gameObject.layer = Layers.MinimapOnly;
		_minimapMarker.gameObject.layer = Layers.MinimapOnly;
        _minimapDirectionArrow.gameObject.layer = Layers.MinimapOnly;
    }
	
	private void LateUpdate()
    {
        Vector3 facingDirection = transform.forward;
        facingDirection.y = 0f;
        
        Vector3 localDirection = _minimapDirectionArrow.rectTransform.parent
                       .InverseTransformDirection(facingDirection);
        
        float angle = -Mathf.Atan2(localDirection.x, localDirection.y) * Mathf.Rad2Deg;
        
        _minimapDirectionArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    // 닉네임 변경 시 MinimapText, 머리위 닉네임마커 값 수정
    public void HandlePlayerNameChanged(FixedString32Bytes oldName, FixedString32Bytes newName) {
		_minimapNameText.text = newName.ToString();
		_nameTag.SetText(newName.ToString());
	}

	public void HandlePlayerColorChanged(Color colorBefore, Color colorAfter) {
		_minimapMarker.color = colorAfter;
	}
}
