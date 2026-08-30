using UnityEngine;
using UnityEngine.Localization;

// 전시물별 설명 문구와 안내 영상 데이터만 보관하고, 실제 표시는 Pedestal과 씬의 공용 튜토리얼 UI에 맡긴다.
// 전시물마다 Canvas를 복제하지 않아 표시와 입력 차단 수명 주기를 한 곳에서 관리한다.
public sealed class WaitingRoomExhibitInteractable : InteractableBase
{
    [SerializeField] private LocalizedString _title;
    [SerializeField] private LocalizedString _subtitle;
    [SerializeField] private LocalizedString _body;
    [SerializeField] private WaitingRoomTutorialInfoView _pedestalInfo;
    [SerializeField] private TutorialFlipbook _flipbook;

    private WaitingRoomObjectTutorialUI _tutorialUI;

    protected override void Awake()
    {
        base.Awake();
        _pedestalInfo.SetContent(_title, _subtitle, _body);
    }

    public override bool CanInteract(GameObject interactor)
    {
        return GetTutorialUI() != null;
    }

    public override void Interact(GameObject interactor)
    {
        GetTutorialUI()?.Open(_title, _subtitle, _body, _flipbook);
    }

    private WaitingRoomObjectTutorialUI GetTutorialUI()
    {
        // 공용 UI는 닫힌 동안 비활성 상태이므로 비활성 오브젝트까지 검색하고, 이후 상호작용에서는 캐시를 재사용한다.
        _tutorialUI ??= FindFirstObjectByType<WaitingRoomObjectTutorialUI>(FindObjectsInactive.Include);
        return _tutorialUI;
    }
}
