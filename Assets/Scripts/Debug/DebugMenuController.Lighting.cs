using TMPro;
using UnityEngine;

// 어두운 밤 컨셉 씬에서 디버깅용 조명을 켜고 끕니다.
public sealed partial class DebugMenuController
{
    private const string DebugLightObjectName = "Debug Directional Light";

    [Header("Lighting")]
    [SerializeField] private TMP_Text _debugLightButtonText;

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

    // 현재 조명 상태에 맞춰 버튼 문구를 갱신합니다.
    private void RefreshDebugLightButtonLabel()
    {
        if (_debugLightButtonText == null) return;

        Light debugLight = FindDebugLight();
        _debugLightButtonText.text = debugLight != null && debugLight.enabled ? "디버그 조명 끄기" : "디버그 조명 켜기";
    }

    // 씬에 배치된 디버그용 조명을 이름으로 찾습니다. 씬마다 없을 수 있습니다.
    private static Light FindDebugLight()
    {
        GameObject debugLightObject = GameObject.Find(DebugLightObjectName);
        return debugLightObject != null ? debugLightObject.GetComponent<Light>() : null;
    }
}
