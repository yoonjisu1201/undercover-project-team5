using UnityEngine;

// 가이드북 아이템. 주우면 바로 가이드북 UI를 연다.
public class GuideBookItem : ItemBase
{
    protected override void OnAdded() => ShowGuideBook();

    public override void OnSelected() => ShowGuideBook();

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
