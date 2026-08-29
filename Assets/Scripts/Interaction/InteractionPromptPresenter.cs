using UnityEngine;
using UnityEngine.Localization;

// 상호작용 안내 문구를 정하고 화면에 띄우는 책임만 가진다. PlayerInteraction 에서 떼어냈다.
//
// "무엇을 할 수 있는지"는 PlayerInteraction 이 알고(조준 대상, 적용 가능한 아이템),
// "그것을 어떻게 보여줄지"는 여기가 정한다. 그래서 PlayerInteraction 은 문구 문자열을
// 조립하지 않고 이 컴포넌트에 결과만 알린다.
//
// InteractionPromptUI 는 씬과 함께 새로 만들어지고 플레이어 오브젝트는 씬 전환에도 살아남는다.
// 그래서 참조를 한 번 잡아두지 않고, 없을 때마다 다시 찾는다.
[RequireComponent(typeof(PlayerInteraction))]
public sealed class InteractionPromptPresenter : MonoBehaviour
{
    private const string LocalizationTable = "Language Table";
    private static readonly LocalizedString StandFromBenchText =
        new LocalizedString(LocalizationTable, "interact_stand_from_bench");

    // 서버 상호작용 결과가 늦게 도착해도 안내 문구가 바로 바뀌도록 잠깐만 재확인하는 구간의 길이.
    private const float RefreshWindow = 0.75f;

    private PlayerInteraction _interaction;
    private PlayerInventory _inventory;
    private InteractionPromptUI _ui;

    private float _refreshUntil;

    private void Awake()
    {
        _interaction = GetComponent<PlayerInteraction>();
        _inventory = GetComponent<PlayerInventory>();
    }

    // 씬에 있는 안내 UI. 없으면 그때그때 다시 찾는다. 라운드가 끝나 파괴되면 참조가 죽으므로
    // 한 번 잡아두는 방식으로는 그 뒤의 모든 안내가 사라진다.
    public InteractionPromptUI ResolveUi()
    {
        if (_ui == null)
        {
            _ui = FindFirstObjectByType<InteractionPromptUI>(FindObjectsInactive.Include);
        }

        return _ui;
    }

    // 인벤토리 이벤트는 문구에만 쓰이므로 여기서 직접 구독한다.
    public void SubscribeInventory()
    {
        if (_inventory == null) return;

        _inventory.OnInventoryChanged += Refresh;
        _inventory.OnSlotSelected += HandleSlotSelected;
    }

    public void UnsubscribeInventory()
    {
        if (_inventory == null) return;

        _inventory.OnInventoryChanged -= Refresh;
        _inventory.OnSlotSelected -= HandleSlotSelected;
    }

    // 재확인 구간 안에서만 매 프레임 다시 계산한다. 상시로 돌릴 필요가 없다.
    public void TickRefreshWindow()
    {
        if (_interaction.CurrentTarget == null || Time.time > _refreshUntil)
        {
            return;
        }

        Refresh();
    }

    // 길게 눌러 완료한 경우에만, 서버 결과가 늦게 와도 문구가 바뀌도록 잠깐 재확인 구간을 연다.
    public void OpenRefreshWindow(float completedThreshold)
    {
        if (completedThreshold <= 0f)
        {
            return;
        }

        _refreshUntil = Time.time + RefreshWindow;
        Refresh();
    }

    public void ShowTemporary(string message, bool showKeyHint = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        ResolveUi()?.ShowTemporaryPrompt(message, showKeyHint);
    }

    public void SetHoldProgress(float progress, bool visible)
    {
        ResolveUi()?.SetUseHoldProgress(progress, visible);
    }

    // 카트를 끄는 동안처럼, 대상 조준과 무관하게 고정 문구를 띄우는 경우.
    public void SetStandingText(string text, bool showKeyHint = true)
    {
        ResolveUi()?.SetInteractionPrompt(text, showKeyHint);
    }

    public void Clear()
    {
        ResolveUi()?.SetInteractionPrompt(null, false);
    }

    // 현재 조준 대상과 들고 있는 아이템 기준으로 안내 문구를 다시 정한다.
    public void Refresh()
    {
        if (_interaction.IsSitting)
        {
            string standText = _interaction.CanStand
                ? StandFromBenchText.GetLocalizedString()
                : null;
            SetStandingText(standText, _interaction.CanStand);
            return;
        }

        if (_interaction.IsInteractionBlocked)
        {
            Clear();
            return;
        }

        InteractableBase target = _interaction.CurrentTarget;

        // 1. 대상을 조준 중이고, 그 대상에 적용 가능한 IInteractionApplier 아이템을 들고 있으면
        //    아이템 쪽 문구가 최우선.
        if (target != null
            && _interaction.TryGetApplierForTarget(target, out ItemBase applierItem, out IInteractionApplier applier))
        {
            SetStandingText(AppendHoldSuffix(applier.InteractionApplyText, applierItem.ItemHoldThreshold), true);
            return;
        }

        // 2. 대상만 조준 중이면 대상 자체 문구.
        if (target != null)
        {
            string interactionText = AppendHoldSuffix(target.GetInteractionText(gameObject), target.InteractHoldThreshold);
            SetStandingText(interactionText, target.ShowInteractionKeyHint(gameObject));
            return;
        }

        // 들고 있는 아이템 안내는 슬롯을 고른 직후 1초만 띄운다(HandleSlotSelected). 상시로 띄우면
        // 조준 대상이 없는 동안 계속 남아 화면을 가린다.
        Clear();
    }

    // 휠·숫자키로 슬롯을 바꾸면 손에 든 아이템 안내를 1초만 보여준다.
    // 사용할 수 있는 아이템이면 사용 문구를("단서 확인 : [E]"), 그 외에는 아이템 이름을 띄운다.
    // 이 안내는 우선순위가 가장 낮아서, 조준 중인 대상이 있으면 그 안내를 가리지 않는다.
    private void HandleSlotSelected(int selectedIndex)
    {
        if (_interaction.CurrentTarget != null || _interaction.IsInteractionBlocked)
        {
            return;
        }

        if (!_inventory.TryGetSelectedItemBase(out ItemBase item) || item.ItemData == null)
        {
            return;
        }

        if (item is IUsable usable)
        {
            ShowTemporary(
                AppendUsageDescription(AppendHoldSuffix(usable.UseText, item.ItemHoldThreshold), item.ItemData),
                true);
            return;
        }

        // 사용 방법이 없는 아이템은 이름만 띄워봐야 알려 줄 게 없으므로 아예 표시하지 않는다.
        if (string.IsNullOrWhiteSpace(item.ItemData.UsageDescription))
        {
            return;
        }

        string selected = new LocalizedString(LocalizationTable, "interact_selected_item")
            .GetLocalizedString(item.ItemData.DisplayName);
        ShowTemporary(AppendUsageDescription(selected, item.ItemData));
    }

    // ItemData에 사용 방법이 적혀 있으면 안내 문구 뒤에 " : "로 이어 붙인다.
    private static string AppendUsageDescription(string message, ItemData itemData)
    {
        if (itemData == null || string.IsNullOrWhiteSpace(itemData.UsageDescription))
        {
            return message;
        }

        return $"{message} : {itemData.UsageDescription}";
    }

    // 길게 눌러야 하는 상호작용이면(threshold > 0) 안내 문구에 길게 누르기 표시를 붙인다.
    // 접미사를 이어 붙이지 않고 인자로 넘긴다. 언어에 따라 앞에 오거나 표현이 달라질 수 있다.
    private static string AppendHoldSuffix(string text, float holdThreshold)
    {
        if (holdThreshold <= 0f || string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return new LocalizedString(LocalizationTable, "interact_hold_suffix").GetLocalizedString(text);
    }
}
