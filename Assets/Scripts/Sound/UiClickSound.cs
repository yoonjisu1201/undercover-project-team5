using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 버튼마다 onClick에 소리를 붙이지 않고, 클릭한 지점 아래에 버튼이 있는지 확인해 소리를 낸다.
// 런타임에 생성되는 화면(미션 UI, 상점 슬롯 등)의 버튼도 자동으로 포함된다.
public class UiClickSound : MonoBehaviour
{
    private readonly List<RaycastResult> _raycastResults = new();

    private void Update()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current == null) return;

        PointerEventData pointerData = new(EventSystem.current)
        {
            position = Mouse.current.position.ReadValue()
        };

        _raycastResults.Clear();
        EventSystem.current.RaycastAll(pointerData, _raycastResults);
        if (_raycastResults.Count == 0) return;

        // EventSystem은 맨 앞 대상에서 부모로 올라가며 처리기를 찾는다. 같은 규칙을 쓴다.
        Button button = _raycastResults[0].gameObject.GetComponentInParent<Button>();
        if (button == null || !button.interactable) return;

        SoundKey key = button.TryGetComponent(out UiClickSoundOverride ovr) ? ovr.Key : SoundKey.Ui_Click;
        SoundManager.Instance?.Play(key);
    }
}
