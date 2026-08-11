using UnityEngine;

// 단서/가이드북 UI를 닫는 처리와, 카트 상호작용을 담당한다.
// 단서/가이드북 각각을 "여는" 반응은 ClueItem/GuideBookItem이 자기 자신의 일로 갖고 있다
// (ItemBase.OnAdded/OnSelected 참고).
public partial class PlayerInteraction
{
    //---------------------------------- Clue ----------------------------------//

    // 현재 선택 슬롯의 아이템이 스스로 반응하게 한다 (단서면 자기 UI를 연다).
    private void TryShowSelectedItemUi()
    {
        if (_inventory != null && _inventory.TryGetSelectedItemBase(out ItemBase item))
        {
            item.OnSelected();
        }
    }

    // 활성화된 단서 UI가 있으면 닫고 true를 반환한다.
    private static bool TryCloseVisibleClue()
    {
        ClueUI[] clueDisplays = FindObjectsByType<ClueUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (ClueUI clueDisplay in clueDisplays)
        {
            if (!clueDisplay.gameObject.activeInHierarchy)
            {
                continue;
            }

            clueDisplay.Close();
            return true;
        }

        return false;
    }

    //-------------------------------- GuideBook -------------------------------//

    // 활성화된 가이드 북 UI가 있으면 닫고 true를 반환한다.
    private static bool TryCloseGuideBook()
    {
        GuideBook[] displays = FindObjectsByType<GuideBook>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (GuideBook display in displays)
        {
            if (!display.gameObject.activeInHierarchy)
            {
                continue;
            }

            display.Close();
            return true;
        }

        return false;
    }


    //---------------------------------- Cart ----------------------------------//

    // 카트를 끌고 있을 때는 다른 오브젝트와 상호작용 불가능하게 한다
    private void UpdateCartInteraction()
    {
        SetCurrentTarget(null);
        _promptUI.SetInteractionPrompt($"{CarryingCart.CartName}카트 놓기");

        // 카트 끄는 도중 상호작용키 다시 누르면 카트를 놓는다.
        if (_actions.Player.Interact.WasPressedThisFrame())
        {
            CarryingCart.ReleaseCart();
            _promptUI.SetInteractionPrompt("");
        }
    }
}
