using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Video;

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
        _tutorialUI ??= FindFirstObjectByType<WaitingRoomObjectTutorialUI>(FindObjectsInactive.Include);
        return _tutorialUI;
    }
}
