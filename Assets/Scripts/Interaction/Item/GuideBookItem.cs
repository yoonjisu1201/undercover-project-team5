using UnityEngine;

// 가이드북 아이템. 주우면 바로 열고, 이후에는 IUsable 경로로 다시 확인할 수 있다.
public class GuideBookItem : ItemBase, IUsable
{
    protected override void OnAdded() => ShowGuideBook();

    public string UseText => "가이드북 확인";
    public string UseCompletedMessage => string.Empty;

    public bool CanUse(GameObject user, out string failReason)
    {
        failReason = null;
        return true;
    }

    public void Use(GameObject user, PlayerInventory inventory, int selectedIndex) { }

    protected override void OnUseCompleted() => ShowGuideBook();

    private void ShowGuideBook()
    {
        GuideBook display = FindFirstObjectByType<GuideBook>(FindObjectsInactive.Include);
        if (display == null)
        {
            Debug.LogWarning("[GuideBookItem] GuideBook 찾지 못했습니다.");
            return;
        }

        display.Show();
    }
}
