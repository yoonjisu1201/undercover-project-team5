using UnityEngine;

// 가이드 북(작전 매뉴얼) UI. 활성화되면 커서/이동 제한 모드로 전환하고, 비활성화되면 해제한다. (ClueUI와 동일한 패턴)
public class GuideBookDisplay : MonoBehaviour
{
    private void OnEnable()
    {
        GameplayUiMode.Instance?.ActivateCursor();
    }

    private void OnDisable()
    {
        GameplayUiMode.Instance?.DeactivateCursor();
    }

    public void Show()
    {
        gameObject.SetActive(true);
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    // X 버튼의 onClick(인스펙터)에 연결한다.
    public void OnButtonClick()
    {
        Close();
    }
}
