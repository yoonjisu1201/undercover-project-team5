using TMPro;
using UnityEngine;

// 어두운 밤 컨셉 씬에서 디버깅용 조명을 켜고 끕니다.
public sealed partial class DebugMenuController
{
    [Header("Lighting")]
    [SerializeField] private Light _debugLight;
    [SerializeField] private TMP_Text _debugLightButtonText;

    // 디버그용 조명을 켜고 끕니다.
    public void OnToggleDebugLightClick()
    {
        if (_debugLight == null)
        {
            ShowStatus("디버그 조명을 찾지 못했습니다.");
            return;
        }

        _debugLight.enabled = !_debugLight.enabled;
        RefreshDebugLightButtonLabel();
        ShowStatus(_debugLight.enabled ? "디버그 조명을 켰습니다." : "디버그 조명을 껐습니다.");
    }

    // 현재 조명 상태에 맞춰 버튼 문구를 갱신합니다.
    private void RefreshDebugLightButtonLabel()
    {
        if (_debugLightButtonText != null)
        {
            _debugLightButtonText.text = _debugLight != null && _debugLight.enabled ? "디버그 조명 끄기" : "디버그 조명 켜기";
        }
    }
}
