using Unity.Collections;
using UnityEngine;

public class PlayerInfoPresenter : MonoBehaviour {

    [Header("=== 캐릭터의 머리 위에 뜨는 닉네임마커 ===")] 
	[SerializeField] private PlayerNameTag _nameTag;
	
	private Player _player;

    // 닉네임 변경 시 MinimapText, 머리위 닉네임마커 값 수정
    public void HandlePlayerNameChanged(FixedString32Bytes oldName, FixedString32Bytes newName) {
		_nameTag.SetText(newName.ToString());
	}
}
