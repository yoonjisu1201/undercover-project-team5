using UnityEngine;

// 아이템을 주웠을 때(또는 선택했을 때) 해당 아이템의 UI를 여닫는다.
// 아이템 종류별로 아래 섹션으로 강하게 구분한다.
public partial class PlayerInteraction
{
    // 주운 아이템 종류에 맞는 UI를 연다. (아이템 추가 시 호출)
    private void HandleItemAdded(string itemId, int _)
    {
        if (TryGetClueIndex(itemId, out int clueIndex))
        {
            ShowClue(clueIndex);
            return;
        }

        // 가이드 북을 주우면 바로 가이드 북 UI를 켠다.
        if (itemId == _guideBookItemId)
        {
            ShowGuideBook();
        }
    }

    //---------------------------------- Clue ----------------------------------//

    // 현재 선택 슬롯의 아이템이 단서면 해당 단서 UI를 연다.
    private void TryShowSelectedClue()
    {
        if (_inventory == null || !_inventory.TryGetSelectedItem(out string itemId))
        {
            return;
        }

        if (TryGetClueIndex(itemId, out int clueIndex))
        {
            ShowClue(clueIndex);
        }
    }

    // 아이템 ID가 단서(예: "Clue3")면 0-based 인덱스를 out으로 반환한다.
    private bool TryGetClueIndex(string itemId, out int clueIndex)
    {
        clueIndex = -1;

        if (string.IsNullOrWhiteSpace(itemId) || !itemId.StartsWith(_clueItemIdPrefix))
        {
            return false;
        }

        string numberText = itemId[_clueItemIdPrefix.Length..].Trim();
        return int.TryParse(numberText, out int clueNumber) &&
               (clueIndex = clueNumber - 1) >= 0;
    }

    private void ShowClue(int clueIndex)
    {
        ClueUI[] clueDisplays = FindObjectsByType<ClueUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        System.Array.Sort(clueDisplays, (left, right) => string.CompareOrdinal(left.name, right.name));

        if (clueIndex >= clueDisplays.Length)
        {
            Debug.LogWarning($"표시할 ClueDisplay가 부족합니다. 단서 번호: {clueIndex + 1}");
            return;
        }

        clueDisplays[clueIndex].gameObject.SetActive(true);
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

    private void ShowGuideBook()
    {
        GuideBook display = FindFirstObjectByType<GuideBook>(FindObjectsInactive.Include);
        if (display == null)
        {
            Debug.LogWarning("[PlayerInteraction] GuideBook 찾지 못했습니다.");
            return;
        }

        display.Show();
    }

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
        _inventoryUI.SetInteractionPrompt($"{CarryingCart.CartName}카트 놓기");

        // 카트 끄는 도중 상호작용키 다시 누르면 카트를 놓는다.
        if (_actions.Player.Interact.WasPressedThisFrame())
        {
            CarryingCart.ReleaseCart();
        }
    }
}
