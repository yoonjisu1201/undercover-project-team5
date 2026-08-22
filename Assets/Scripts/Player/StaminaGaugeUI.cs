using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 화면 우상단에 떠 있는 개인 스태미나 게이지. PlayerCameraController와 같은 방식으로,
// Canvas 자체는 모든 인스턴스에 붙어있지만 오너가 아니면 꺼둔다.
public class StaminaGaugeUI : NetworkBehaviour
{
    [SerializeField] private GameObject _canvasRoot;
    [SerializeField] private Slider _staminaSlider;
    [SerializeField] private Image _coreLampImage;
    // 채움을 자르는 경계는 수직이고 바는 비스듬하다. 그래서 마지막에 좌하단 삼각형이 남는다.
    // 표시 비율을 이 값만큼 앞당겨 끝내서 그 삼각형이 보이기 전에 다 비도록 한다.
    [SerializeField, Range(0f, 0.2f)] private float _emptyOvershoot = 0.05f;

    [Header("게이지 색상")]
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
    private Image _fillImage;
    // 램프의 평상시 색은 Inspector 값을 그대로 쓴다. 경고 때만 여기서 벗어난다.
    private Color _coreLampRestColor = Color.white;
    private float _displayStamina;
    private float _targetStamina;

    private void Awake()
    {
        _playerStamina = GetComponent<PlayerStamina>();

        if (_coreLampImage != null)
        {
            _coreLampRestColor = _coreLampImage.color;
        }

        CacheSliderImages();
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
        ApplyFillAmount();
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
        if (!IsOwner || _staminaSlider == null || _playerStamina == null)
        {
            return;
        }

        // 보간은 표시값이 실제값을 시간상수만큼 뒤따라가게 만든다. 계속 소모되는 동안에는 그 지연이
        // 소모 속도에 비례해 쌓여서, 실제로 0이 돼도 게이지에 몇 % 가 남은 채로 회복이 시작된다.
        // 다 비었을 때만 보간을 건너뛰어 끝까지 닳은 것으로 보이게 한다.
        if (_playerStamina.IsEmpty)
        {
            _displayStamina = 0f;
        }
        else
        {
            float t = 1f - Mathf.Exp(-_fillLerpSpeed * Time.deltaTime);
            _displayStamina = Mathf.Lerp(_displayStamina, _targetStamina, t);
        }

        _staminaSlider.SetValueWithoutNotify(_displayStamina);
        ApplyFillAmount();
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

        Transform fill = _staminaSlider.transform.Find("Fill Area/Fill");

        if (fill != null)
        {
            _fillImage = fill.GetComponent<Image>();
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

        if (_fillImage != null)
        {
            _fillImage.color = _normalFillColor;
            _fillImage.raycastTarget = false;
        }
    }

    // 남은 비율을 채움 길이로 옮긴다. 오버슛만큼 앞당겨 끝내므로 100%는 그대로 꽉 차고,
    // 오버슛 지점에서 이미 0이 된다.
    private void ApplyFillAmount()
    {
        if (_fillImage == null)
        {
            return;
        }

        float max = _staminaSlider.maxValue;
        float ratio = max <= 0f ? 0f : Mathf.Clamp01(_displayStamina / max);
        float span = 1f - _emptyOvershoot;
        _fillImage.fillAmount = span <= 0f ? 0f : Mathf.Clamp01((ratio - _emptyOvershoot) / span);
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

        // 임계값 아래로 떨어지면 채움과 소켓 램프가 같은 박자로 깜빡인다.
        // 세기를 서서히 올리지 않고 바로 또렷하게 깜빡여야 플레이어가 알아챈다.
        float pulse = ratio < _lowStaminaRatio
            ? Mathf.Abs(Mathf.Sin(Time.unscaledTime * _lowPulseSpeed))
            : 0f;

        if (_fillImage != null)
        {
            Color lowColor = Color.Lerp(_normalFillColor, _lowFillColor, lowBlend);
            Color color = Color.Lerp(lowColor, _blockedFillColor, blockedBlend);
            _fillImage.color = Color.Lerp(color, _emptyPulseColor, pulse);
        }

        if (_coreLampImage != null)
        {
            _coreLampImage.color = Color.Lerp(_coreLampRestColor, _emptyPulseColor, pulse);
        }
    }

}
