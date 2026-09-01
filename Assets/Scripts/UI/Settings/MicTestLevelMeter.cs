using UnityEngine;
using UnityEngine.UI;

public sealed class MicTestLevelMeter : MonoBehaviour
{
    [SerializeField] private Image[] _bars;

    [Header("Colors")]
    [SerializeField] private Color _inactiveColor = new(0.15f, 0.15f, 0.15f);   // 막대가 비활성화 상태일 때의 색상
    [SerializeField] private Color _normalColor = new(0.2f, 0.8f, 0.3f);
    [SerializeField] private Color _warningColor = new(1f, 0.75f, 0.1f);
    [SerializeField] private Color _dangerColor = new(1f, 0.25f, 0.2f);

    [Header("Animation")]
    [SerializeField] private float _riseSpeed = 15f;    // 막대가 상승할 때의 속도
    [SerializeField] private float _fallSpeed = 5f;     // 막대가 하강할 때의 속도

    private const float MinBarHeight = 20f;     // 막대의 최소 높이
    private const float MaxBarHeight = 46f;     // 막대의 최대 높이

    private float _displayEnergy;

    private void Update()
    {
        float targetEnergy = GetMicEnergy();

        float speed = targetEnergy > _displayEnergy ? _riseSpeed : _fallSpeed;

        _displayEnergy = Mathf.MoveTowards(_displayEnergy, targetEnergy, speed * Time.unscaledDeltaTime
        );

        RefreshBars(_displayEnergy);
    }

    // 마이크 테스트는 Vivox 채널을 거치지 않고 마이크 입력을 바로 들려주므로,
    // 막대도 그 입력에서 계산한 음량을 쓴다.
    private float GetMicEnergy()
    {
        VivoxManager manager = VivoxManager.Instance;
        return manager != null ? manager.MicMonitorEnergy01 : 0f;
    }

    private void RefreshBars(float energy)
    {
        bool isTesting = VivoxManager.Instance != null && VivoxManager.Instance.IsMicTesting;

        for (int i = 0; i < _bars.Length; i++)
        {
            if (_bars[i] == null)
            {
                continue;
            }

            float barEnergy = 0f;

            // 막대마다 서로 다른 움직임을 만들기 위한 파형
            if (isTesting)
            {
                float wave = Mathf.PerlinNoise(i * 0.35f, Time.unscaledTime * 8f);
                barEnergy = Mathf.Clamp01(energy * wave * 1.5f);
            }

            float height = Mathf.Lerp(MinBarHeight, MaxBarHeight, barEnergy);

            RectTransform barRect = _bars[i].rectTransform;
            barRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

            SetBarColor(_bars[i], barEnergy);
        }
    }

    private void SetBarColor(Image bar, float energy)   // 막대의 색상을 에너지 값에 따라 변경
    {
        if (energy >= 0.85f)
        {
            bar.color = _dangerColor;
        }
        else if (energy >= 0.65f)
        {
            bar.color = _warningColor;
        }
        else if (energy > 0.05f)
        {
            bar.color = _normalColor;
        }
        else
        {
            bar.color = _inactiveColor;
        }
    }
}
