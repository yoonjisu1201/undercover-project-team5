using TMPro;
using UnityEngine.Scripting;
using UnityEngine.UI;

// 지하철 노선도에서 카드를 받을 슬롯을 관리합니다.
[Preserve]
public sealed class StationDropSlot : UIDropSlot
{
    public void Initialize(SubwayRouteMiniGame owner, int slotIndex, Image background, TMP_Text placeholder)
    {
        InitializeSlot(owner, slotIndex, background, placeholder);
    }
}
