using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Video;

// 전시물별 설명 이미지와 영상 데이터만 보관하고, 실제 표시는 씬의 공용 튜토리얼 UI에 맡긴다.
// 전시물마다 Canvas와 VideoPlayer를 복제하지 않아 표시와 입력 차단 수명 주기를 한 곳에서 관리한다.
public sealed class WaitingRoomExhibitInteractable : InteractableBase
{
    [SerializeField] private LocalizedTexture _informationImage;
    [SerializeField] private VideoClip _videoClip;

    private WaitingRoomObjectTutorialUI _tutorialUI;

    public override string InteractionText => "정보 보기";

    public override bool CanInteract(GameObject interactor)
    {
        return GetTutorialUI() != null;
    }

    public override void Interact(GameObject interactor)
    {
        GetTutorialUI()?.Open(_informationImage, _videoClip);
    }

    private WaitingRoomObjectTutorialUI GetTutorialUI()
    {
        // 공용 UI는 닫힌 동안 비활성 상태이므로 비활성 오브젝트까지 검색하고, 이후 상호작용에서는 캐시를 재사용한다.
        _tutorialUI ??= FindFirstObjectByType<WaitingRoomObjectTutorialUI>(FindObjectsInactive.Include);
        return _tutorialUI;
    }
}
