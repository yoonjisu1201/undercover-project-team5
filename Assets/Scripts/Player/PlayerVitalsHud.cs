using TMPro;
using Unity.Netcode;
using UnityEngine;

// 화면 구석에 떠 있는 개인 HUD. HP는 심장 박동 파형, ST(스태미나)는 칸 게이지로 보여주고
// 각 줄 끝에 퍼센트를 적는다. PlayerCameraController와 같은 방식으로 Canvas 자체는 모든
// 인스턴스에 붙어 있고 오너가 아니면 꺼둔다.
public sealed class PlayerVitalsHud : NetworkBehaviour
{
    [SerializeField] private GameObject _canvasRoot;

    [Header("HP")]
    [SerializeField] private EcgTraceGraphic _hpTrace;
    [SerializeField] private TMP_Text _hpPercentText;

    [Header("ST")]
    [SerializeField] private SegmentedBarGraphic _staminaBar;
    [SerializeField] private TMP_Text _staminaLabelText;
    [SerializeField] private TMP_Text _staminaPercentText;

    [Header("HP 색상/반응")]
    [SerializeField] private Color _hpNormalColor = new(0.94f, 0.31f, 0.24f, 1f);
    [SerializeField] private Color _hpLowColor = new(1f, 0.12f, 0.08f, 1f);
    [SerializeField] private Color _hpDownedColor = new(0.42f, 0.42f, 0.45f, 0.8f);
    // 심장 소리가 없을 때의 심박수. 저체력은 화면 비네트가 알리는 몫이라 여기서 겹쳐 알리지 않는다.
    [SerializeField, Range(20f, 120f)] private float _restingBpm = 68f;

    [Header("ST 색상/반응")]
    [SerializeField] private Color _staminaNormalColor = new(0.95f, 0.7f, 0.16f, 1f);
    [SerializeField] private Color _staminaLowColor = new(1f, 0.42f, 0.08f, 1f);
    [SerializeField] private Color _staminaEmptyColor = new(1f, 0.12f, 0.06f, 1f);
    [SerializeField, Range(0.05f, 0.5f)] private float _lowStaminaRatio = 0.2f;

    [Header("심장 소리 연동")]
    // 심장 소리가 들리는 동안에는 그래프도 같이 격해져야 소리와 화면이 한 몸으로 읽힌다.
    [SerializeField, Range(0.1f, 1f)] private float _restingAmplitude = 0.85f;
    [SerializeField, Range(0.1f, 1f)] private float _heartbeatAmplitude = 1f;
    [SerializeField, Min(0.5f)] private float _restingThickness = 2.5f;
    [SerializeField, Min(0.5f)] private float _heartbeatThickness = 4f;
    // 소리 쪽 크로스페이드가 0.5~0.6초라, 그래프도 같은 정도로 올라와야 따로 움직이지 않는다.
    [SerializeField, Min(0.5f)] private float _heartbeatBlendSpeed = 4f;

    [Header("공통")]
    [SerializeField, Min(1f)] private float _fillLerpSpeed = 14f;
    [SerializeField, Min(0f)] private float _lowPulseSpeed = 8f;

    private PlayerHealth _playerHealth;
    private PlayerStamina _playerStamina;
    private PlayerHeartbeat _playerHeartbeat;

    private float _displayHpRatio;
    private float _targetHpRatio;
    private float _displayStaminaRatio;
    private float _targetStaminaRatio;

    // 퍼센트는 정수로만 바뀌므로 값이 실제로 넘어갈 때만 문자열을 새로 만든다.
    private int _shownHpPercent = -1;
    private int _shownStaminaPercent = -1;

    private bool _isDowned;

    // 심장 소리가 들리는 정도. 0/1 을 그대로 쓰면 소리는 페이드인데 그래프만 툭 바뀐다.
    private float _heartbeatBlend;

    private void Awake()
    {
        _playerHealth = GetComponent<PlayerHealth>();
        _playerStamina = GetComponent<PlayerStamina>();
        _playerHeartbeat = GetComponent<PlayerHeartbeat>();
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

        if (_playerHealth == null || _playerStamina == null || _hpTrace == null || _staminaBar == null)
        {
            Debug.LogError("[PlayerVitalsHud] 개인 HUD 참조가 비어 있습니다.");
            return;
        }

        if (_canvasRoot != null)
        {
            _canvasRoot.SetActive(true);
            _canvasRoot.transform.localScale = Vector3.one;
        }

        // 늦게 들어온 클라이언트는 스폰 시점에 이미 동기화된 현재 값으로 시작해야 한다.
        // 보간 없이 바로 맞춰두지 않으면 최대치에서 실제값까지 훑고 내려오는 게 보인다.
        _isDowned = _playerHealth.IsDowned;
        _targetHpRatio = ReadHpRatio();
        _displayHpRatio = _targetHpRatio;
        _targetStaminaRatio = ReadStaminaRatio();
        _displayStaminaRatio = _targetStaminaRatio;

        ApplyHp(0f);
        ApplyStamina();

        _playerHealth.HpChanged += HandleHpChanged;
        _playerHealth.DownedStateChanged += HandleDownedStateChanged;
        _playerStamina.StaminaChanged += HandleStaminaChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        if (_playerHealth != null)
        {
            _playerHealth.HpChanged -= HandleHpChanged;
            _playerHealth.DownedStateChanged -= HandleDownedStateChanged;
        }

        if (_playerStamina != null)
        {
            _playerStamina.StaminaChanged -= HandleStaminaChanged;
        }
    }

    private void Update()
    {
        if (!IsOwner || _playerHealth == null || _playerStamina == null) return;

        float t = 1f - Mathf.Exp(-_fillLerpSpeed * Time.deltaTime);

        _displayHpRatio = Mathf.Lerp(_displayHpRatio, _targetHpRatio, t);

        // 보간은 표시값이 실제값을 시간상수만큼 뒤따라가게 만든다. 계속 소모되는 동안에는 그 지연이
        // 소모 속도에 비례해 쌓여서, 실제로 0이 돼도 게이지에 몇 % 가 남은 채로 회복이 시작된다.
        // 다 비었을 때만 보간을 건너뛰어 끝까지 닳은 것으로 보이게 한다.
        _displayStaminaRatio = _playerStamina.IsEmpty
            ? 0f
            : Mathf.Lerp(_displayStaminaRatio, _targetStaminaRatio, t);

        // 귀에 들리는 박동과 같은 박자로 뛰게 하려고 소리 쪽 단계를 그대로 읽어온다.
        float audibleBpm = _playerHeartbeat != null ? _playerHeartbeat.CurrentBpm : 0f;
        float blendTarget = audibleBpm > 0f && !_isDowned ? 1f : 0f;

        _heartbeatBlend = Mathf.Lerp(_heartbeatBlend, blendTarget,
            1f - Mathf.Exp(-_heartbeatBlendSpeed * Time.deltaTime));

        ApplyHp(audibleBpm);
        ApplyStamina();
    }

    private void HandleHpChanged(float previousValue, float newValue)
    {
        _targetHpRatio = _playerHealth.MaxHp <= 0f ? 0f : Mathf.Clamp01(newValue / _playerHealth.MaxHp);
    }

    private void HandleDownedStateChanged(bool previousValue, bool newValue)
    {
        _isDowned = newValue;
    }

    private void HandleStaminaChanged(float previousValue, float newValue)
    {
        _targetStaminaRatio = _playerStamina.MaxStamina <= 0f
            ? 0f
            : Mathf.Clamp01(newValue / _playerStamina.MaxStamina);
    }

    private float ReadHpRatio()
    {
        return _playerHealth.MaxHp <= 0f ? 0f : Mathf.Clamp01(_playerHealth.CurrentHp / _playerHealth.MaxHp);
    }

    private float ReadStaminaRatio()
    {
        return _playerStamina.MaxStamina <= 0f
            ? 0f
            : Mathf.Clamp01(_playerStamina.CurrentStamina / _playerStamina.MaxStamina);
    }

    private void ApplyHp(float audibleBpm)
    {
        _hpTrace.IsFlatline = _isDowned;

        // 다운 상태에서는 파형이 평평해지고 색이 죽는다. 게이지가 비었음을 파형으로 말하는 방식이다.
        if (_isDowned)
        {
            _hpTrace.color = _hpDownedColor;
            SetTextColor(_hpPercentText, _hpDownedColor);
            SetPercent(_hpPercentText, 0f, ref _shownHpPercent);
            return;
        }

        // 파형은 귀에 들리는 심장 소리에만 반응한다. HP가 낮다고 해서 빨라지지 않는다 —
        // 저체력 경고는 화면 비네트가 맡고, 여기까지 붉어지면 소리가 멎어도 돌아오지 않는 것처럼 보인다.
        _hpTrace.BeatsPerMinute = audibleBpm > 0f ? audibleBpm : _restingBpm;
        _hpTrace.Amplitude = Mathf.Lerp(_restingAmplitude, _heartbeatAmplitude, _heartbeatBlend);
        _hpTrace.LineThickness = Mathf.Lerp(_restingThickness, _heartbeatThickness, _heartbeatBlend);

        // 소리가 들리는 동안에는 색도 경고색 쪽으로 당겨서 파형이 더 도드라지게 한다.
        Color hpColor = Color.Lerp(_hpNormalColor, _hpLowColor, _heartbeatBlend);

        _hpTrace.color = hpColor;
        SetTextColor(_hpPercentText, hpColor);
        SetPercent(_hpPercentText, _displayHpRatio, ref _shownHpPercent);
    }

    private void ApplyStamina()
    {
        _staminaBar.Ratio = _displayStaminaRatio;

        float lowBlend = Mathf.InverseLerp(_lowStaminaRatio, 0f, _displayStaminaRatio);
        Color staminaColor = Color.Lerp(_staminaNormalColor, _staminaLowColor, lowBlend);
        // 잠긴 동안에는 남은 양과 무관하게 깜빡인다. 깜빡임이 "지금은 못 달린다"를 알리는 신호다.
        float pulse = _playerStamina.IsRedZonePenalized
            ? Mathf.Abs(Mathf.Sin(Time.unscaledTime * _lowPulseSpeed))
            : PulseAmount(_displayStaminaRatio, _lowStaminaRatio);

        Color pulsedStaminaColor = Color.Lerp(staminaColor, _staminaEmptyColor, pulse);

        _staminaBar.color = pulsedStaminaColor;
        SetTextColor(_staminaLabelText, pulsedStaminaColor);
        SetTextColor(_staminaPercentText, pulsedStaminaColor);
        SetPercent(_staminaPercentText, _displayStaminaRatio, ref _shownStaminaPercent);
    }

    // 임계값 아래로 떨어지면 바로 또렷하게 깜빡여야 플레이어가 알아챈다. 세기를 서서히 올리지 않는다.
    private float PulseAmount(float ratio, float threshold)
    {
        return ratio < threshold ? Mathf.Abs(Mathf.Sin(Time.unscaledTime * _lowPulseSpeed)) : 0f;
    }

    // 같은 색을 다시 넣으면 TMP가 메시를 다시 만들므로 바뀔 때만 넣는다.
    private static void SetTextColor(TMP_Text text, Color textColor)
    {
        if (text == null || text.color == textColor) return;

        text.color = textColor;
    }

    // 1이라도 남아 있으면 0%로 보이지 않게 올림한다.
    private static void SetPercent(TMP_Text text, float ratio, ref int shownPercent)
    {
        if (text == null) return;

        int percent = Mathf.Clamp(Mathf.CeilToInt(ratio * 100f), 0, 100);

        if (percent == shownPercent) return;

        shownPercent = percent;
        text.text = percent + "<size=62%>%</size>";
    }
}
