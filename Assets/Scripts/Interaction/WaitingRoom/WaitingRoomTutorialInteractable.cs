using UnityEngine;
using UnityEngine.Localization;

// 그림 튜토리얼 Pedestal별 이미지 데이터만 보관하고, 실제 표시는 씬의 공용 그림 튜토리얼 UI에 맡긴다.
// Pedestal마다 Canvas를 복제하지 않아 동일한 표시 방식과 입력 차단 수명 주기를 한 곳에서 관리한다.
public sealed class WaitingRoomTutorialInteractable : InteractableBase
{
    [SerializeField] private LocalizedTexture _informationImage;

    private WaitingRoomTutorialUI _tutorialUI;

    public override string InteractionText => "정보 보기";

    public override bool CanInteract(GameObject interactor)
    {
        return GetTutorialUI() != null;
    }

    public override void Interact(GameObject interactor)
    {
        GetTutorialUI()?.Open(_informationImage);
    }

    private WaitingRoomTutorialUI GetTutorialUI()
    {
        // 공용 UI는 닫힌 동안 비활성 상태이므로 비활성 오브젝트까지 검색하고, 이후에는 캐시를 재사용한다.
        _tutorialUI ??= FindFirstObjectByType<WaitingRoomTutorialUI>(FindObjectsInactive.Include);
        return _tutorialUI;
    }
}
