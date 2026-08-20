using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 화면 우상단에 떠 있는 개인 스태미나 게이지. PlayerCameraController와 같은 방식으로,
// Canvas 자체는 모든 인스턴스에 붙어있지만 오너가 아니면 꺼둔다.
public class StaminaGaugeUI : NetworkBehaviour
{
    [SerializeField] private GameObject _canvasRoot;
    [SerializeField] private Slider _staminaSlider;

    [Header("게이지 색상")]
    [SerializeField] private Color _backgroundColor = new(0.01f, 0.025f, 0.03f, 0.86f);
    [SerializeField] private Color _normalFillColor = new(1f, 0.82f, 0.18f, 0.96f);
    [SerializeField] private Color _lowFillColor = new(1f, 0.48f, 0.08f, 0.98f);
    [SerializeField] private Color _blockedFillColor = new(1f, 0.08f, 0.04f, 0.98f);
    [SerializeField] private Color _emptyPulseColor = new(1f, 0.18f, 0.08f, 0.34f);

    [Header("게이지 반응")]
    [SerializeField, Range(0.05f, 0.5f)] private float _blockedStaminaRatio = 0.2f;
    [SerializeField, Range(0.05f, 0.5f)] private float _lowStaminaRatio = 0.28f;
    [SerializeField, Min(1f)] private float _fillLerpSpeed = 14f;
    [SerializeField, Min(0f)] private float _lowPulseSpeed = 8f;

    private PlayerStamina _playerStamina;
    private RectTransform _sliderRect;
    private Image _backgroundImage;
    private Image _fillImage;
    private Image _warningPulseImage;
    private Image _coreLampImage;
    private readonly Image[] _chargeSegments = new Image[6];
    private float _displayStamina;
    private float _targetStamina;

    private void Awake()
    {
        _playerStamina = GetComponent<PlayerStamina>();
        CacheSliderImages();
        CacheGaugeImages();
        ApplyStaticStyle();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            if (_canvasRoot != null)
            {
                _canvasRoot.SetActive(false);
            }
            return;
        }

        if (_canvasRoot != null)
        {
            _canvasRoot.SetActive(true);
            _canvasRoot.transform.localScale = Vector3.one;
        }

        if (_playerStamina == null || _staminaSlider == null)
        {
            Debug.LogError("[StaminaGaugeUI] 스태미나 게이지 참조가 비어 있습니다.");
            return;
        }

        _staminaSlider.maxValue = _playerStamina.MaxStamina;
        _targetStamina = _playerStamina.CurrentStamina;
        _displayStamina = _targetStamina;
        _staminaSlider.SetValueWithoutNotify(_displayStamina);
        ApplyDynamicStyle();

        _playerStamina.StaminaChanged += HandleStaminaChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner || _playerStamina == null) return;

        _playerStamina.StaminaChanged -= HandleStaminaChanged;
    }

    private void Update()
    {
        if (!IsOwner || _staminaSlider == null)
        {
            return;
        }

        float t = 1f - Mathf.Exp(-_fillLerpSpeed * Time.deltaTime);
        _displayStamina = Mathf.Lerp(_displayStamina, _targetStamina, t);
        _staminaSlider.SetValueWithoutNotify(_displayStamina);
        ApplyDynamicStyle();
    }

    private void HandleStaminaChanged(float previousValue, float newValue)
    {
        _targetStamina = newValue;
    }

    private void CacheSliderImages()
    {
        if (_staminaSlider == null)
        {
            return;
        }

        _sliderRect = _staminaSlider.GetComponent<RectTransform>();
        Transform background = _staminaSlider.transform.Find("Background");
        Transform fill = _staminaSlider.transform.Find("Fill Area/Fill");

        if (background != null)
        {
            _backgroundImage = background.GetComponent<Image>();
        }

        if (fill != null)
        {
            _fillImage = fill.GetComponent<Image>();
        }
    }

    private void CacheGaugeImages()
    {
        if (_sliderRect == null)
        {
            return;
        }

        _warningPulseImage = FindGaugeImage("StaminaGauge_WarningPulse");
        _coreLampImage = FindGaugeImage("StaminaGauge_CoreLamp");

        for (int i = 0; i < _chargeSegments.Length; i++)
        {
            _chargeSegments[i] = FindGaugeImage($"StaminaGauge_ChargeSegment_{i + 1}");
        }
    }

    private void ApplyStaticStyle()
    {
        if (_canvasRoot != null)
        {
            _canvasRoot.transform.localScale = Vector3.one;
        }

        if (_staminaSlider != null)
        {
            _staminaSlider.interactable = false;
            _staminaSlider.transition = Selectable.Transition.None;
        }

        if (_backgroundImage != null)
        {
            _backgroundImage.color = _backgroundColor;
            _backgroundImage.raycastTarget = false;
        }

        if (_fillImage != null)
        {
            _fillImage.color = _normalFillColor;
            _fillImage.raycastTarget = false;
        }
    }

    private void ApplyDynamicStyle()
    {
        if (_staminaSlider == null)
        {
            return;
        }

        float ratio = _staminaSlider.maxValue <= 0f ? 0f : Mathf.Clamp01(_displayStamina / _staminaSlider.maxValue);
        float blockedBlend = Mathf.InverseLerp(_blockedStaminaRatio, 0f, ratio);
        float lowBlend = Mathf.InverseLerp(_lowStaminaRatio, 0f, ratio);

        if (_fillImage != null)
        {
            Color lowColor = Color.Lerp(_normalFillColor, _lowFillColor, lowBlend);
            _fillImage.color = Color.Lerp(lowColor, _blockedFillColor, blockedBlend);
        }

        if (_warningPulseImage != null)
        {
            float pulse = Mathf.Abs(Mathf.Sin(Time.unscaledTime * _lowPulseSpeed));
            Color color = _emptyPulseColor;
            color.a *= Mathf.Max(lowBlend * pulse, blockedBlend * (0.35f + 0.65f * pulse));
            _warningPulseImage.color = color;
        }

        if (_coreLampImage != null)
        {
            float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * _lowPulseSpeed));
            Color lampColor = Color.Lerp(_normalFillColor, _blockedFillColor, blockedBlend);
            lampColor.a *= pulse;
            _coreLampImage.color = lampColor;
        }

        for (int i = 0; i < _chargeSegments.Length; i++)
        {
            Image segment = _chargeSegments[i];
            if (segment == null)
            {
                continue;
            }

            float segmentThreshold = (i + 1f) / (_chargeSegments.Length + 1f);
            float activeAlpha = ratio >= segmentThreshold ? 0.78f : 0.24f;
            Color segmentColor = Color.Lerp(new Color(0.02f, 0.018f, 0.012f, 1f), _blockedFillColor, blockedBlend);
            segmentColor.a = activeAlpha;
            segment.color = segmentColor;
        }
    }

    private Image FindGaugeImage(string childName)
    {
        Transform child = _sliderRect.Find(childName);
        return child == null ? null : child.GetComponent<Image>();
    }
}
