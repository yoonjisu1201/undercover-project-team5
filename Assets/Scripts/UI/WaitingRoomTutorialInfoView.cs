using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;

// Pedestal과 상세 UI가 공통으로 사용하는 튜토리얼 정보 표시 영역이다.
// 각 문구의 Localization 갱신은 LocalizeStringEvent에 맡기고, 이 컴포넌트는 표시할 항목만 교체한다.
public sealed class WaitingRoomTutorialInfoView : MonoBehaviour
{
    [SerializeField] private LocalizeStringEvent _title;
    [SerializeField] private LocalizeStringEvent _subtitle;
    [SerializeField] private LocalizeStringEvent _body;

    public void SetContent(
        LocalizedString title,
        LocalizedString subtitle,
        LocalizedString body)
    {
        SetStringReference(_title, title);
        SetStringReference(_subtitle, subtitle);
        SetStringReference(_body, body);
    }

    private static void SetStringReference(
        LocalizeStringEvent target,
        LocalizedString value)
    {
        target.StringReference = value;
        target.RefreshString();
    }
}
