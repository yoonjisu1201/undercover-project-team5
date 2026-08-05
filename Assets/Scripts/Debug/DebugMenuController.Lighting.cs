using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 어두운 밤 컨셉 씬에서 디버깅용 조명을 켜고 끕니다.
public sealed partial class DebugMenuController
{
    private const string DebugLightObjectName = "Debug Directional Light";

    [Header("Lighting")]
    [SerializeField] private TMP_Text _debugLightButtonText;
    [SerializeField] private Button _debugLightButton;

    // 디버그용 조명을 켜고 끕니다.
    public void OnToggleDebugLightClick()
    {
        Light debugLight = FindDebugLight();
        if (debugLight == null)
        {
            ShowStatus("디버그 조명을 찾지 못했습니다.");
            return;
        }

        debugLight.enabled = !debugLight.enabled;
        RefreshDebugLightButtonLabel();
        ShowStatus(debugLight.enabled ? "디버그 조명을 켰습니다." : "디버그 조명을 껐습니다.");
    }

    // 현재 조명 상태에 맞춰 버튼 문구와 색을 갱신합니다. '조명 켜기'는 초록, '조명 끄기'는 빨강입니다.
    private void RefreshDebugLightButtonLabel()
    {
        Light debugLight = FindDebugLight();
        bool isLightOn = debugLight != null && debugLight.enabled;

        if (_debugLightButtonText != null)
        {
            _debugLightButtonText.text = isLightOn ? "조명 끄기" : "조명 켜기";
        }

        SetOnOffButtonColor(_debugLightButton, !isLightOn);
    }

    // 씬에 배치된 디버그용 조명을 이름으로 찾습니다. 씬마다 없을 수 있습니다.
    private static Light FindDebugLight()
    {
        GameObject debugLightObject = GameObject.Find(DebugLightObjectName);
        return debugLightObject != null ? debugLightObject.GetComponent<Light>() : null;
    }
}
