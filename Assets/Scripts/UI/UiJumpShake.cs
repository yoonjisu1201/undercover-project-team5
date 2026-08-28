using UnityEngine;

// 점프와 착지에 UI 를 반응시킨다. 화면이 몸에 느슨하게 붙어 관성으로 뒤처지는 것처럼 보이게 한다.
//
// 땅을 떠나면 위로 들려서 뜬 동안 그대로 있고, 발이 닿으면 내려오면서 흔들리며 안착한다.
//
// 수직 속도에 비례해 어긋나게도 해봤지만(관성 표현), 이륙하는 순간의 움직임이 점프를 무겁게
// 만들어서 쓰지 않는다. 뜬 동안 일정한 높이를 유지하는 편이 경쾌하다.
//
// 흔들 대상마다 하나씩 붙인다. 캔버스 하나를 통째로 흔들면 화면 전체를 덮는 연출(혈흔 오버레이 등)
// 까지 같이 밀려서 가장자리가 드러나기 때문이다.
//
// 오너 판정은 PlayerMoveSample 이 하고 여기는 화면의 일만 한다.
[RequireComponent(typeof(RectTransform))]
public sealed class UiJumpShake : MonoBehaviour
{
    // 설정창에서 끌 수 있다. 저장은 GameSettingsMenu 가 PlayerPrefs 로 맡고 여기는 상태만 받는다.
    // 대상이 씬 곳곳에 흩어져 있어서 인스턴스마다 값을 넣는 대신 static 으로 공유한다.
    public static bool IsEnabled { get; set; } = true;

    [Header("점프 — 들림")]
    [Tooltip("공중에 떠 있는 동안 들려 있는 높이(픽셀). 착지하면 제자리로 내려온다.")]
    [SerializeField, Min(0f)] private float _liftStrength = 3f;

    [Tooltip("들리고 내려오는 속도. 높이면 즉각적으로, 낮추면 늘어지듯 따라온다.")]
    [SerializeField, Min(1f)] private float _liftFollowSpeed = 16f;

    [Header("착지 — 흔들림")]
    [Tooltip("흔들리는 시간(초).")]
    [SerializeField, Min(0.01f)] private float _duration = 0.26f;

    [Tooltip("세로 흔들림의 최대 크기(픽셀). 착지는 수직 충격이라 세로를 기준으로 잡는다.")]
    [SerializeField, Min(0f)] private float _strength = 4f;

    [Tooltip("가로 흔들림을 세로에 대한 비율로 준다. 1 로 두면 사방으로 같은 크기로 흔들린다.")]
    [SerializeField, Range(0f, 1f)] private float _horizontalRatio = 0.45f;

    [Tooltip("흔들리는 속도. 높이면 잔진동처럼 잘게 떨린다.")]
    [SerializeField, Min(1f)] private float _frequency = 26f;

    [Tooltip("함께 흔들릴 기울기의 최대 각도(도). 0 이면 위치만 흔들린다. "
        + "텍스트가 주인 UI 에서는 1도만 기울어도 글자 외곽이 떨려 보이므로 0 으로 두는 편이 낫다.")]
    [SerializeField, Range(0f, 10f)] private float _rotationStrength = 0.35f;

    [Tooltip("이 낙하 속도(m/s) 이하로 내려오면 흔들리지 않는다. 평지 점프는 약 15.7 로 착지한다. "
        + "두 값을 좁게(0 ~ 1) 두면 낙하 속도와 무관하게 착지마다 전체 세기로 흔들린다.")]
    [SerializeField, Min(0f)] private float _minImpactSpeed;

    [Tooltip("이 낙하 속도부터 최대 세기로 흔들린다. 평지 점프 15.7, 약 7m 를 더 떨어지면 25 다.")]
    [SerializeField, Min(0.1f)] private float _maxImpactSpeed = 1f;

    private RectTransform _rect;

    // 움직이지 않을 때의 자리. Awake 에서 한 번만 잡는다. 진행 중에 다시 잡으면 어긋난 위치가
    // 원래 자리로 굳어서 UI 가 제자리로 돌아오지 못한다.
    private Vector2 _restPosition;
    private Quaternion _restRotation;

    // 지금 공중에 떠 있는지. 땅에 있는 동안은 어긋남을 0 으로 둔다. 접지 상태에서도 수직 속도가
    // 미세하게 흔들려서, 속도만으로 판단하면 걸어다니는 내내 UI 가 잘게 떨린다.
    private bool _isAirborne;

    // 현재 들려 있는 높이. 목표(뜬 상태면 _liftStrength, 아니면 0)를 따라 부드럽게 오간다.
    // 착지 흔들림과는 더해서 적용한다 — 어느 하나로 덮어쓰면 그 순간 위치가 튄다.
    private float _liftOffset;

    // 음수 = 흔들리는 중이 아님.
    private float _shakeElapsed = -1f;

    // 흔들릴 때마다 위상을 새로 뽑는다. 고정해두면 뛸 때마다 똑같은 모양으로 흔들려서 눈에 띈다.
    private float _phaseX;
    private float _phaseY;

    // 이번 착지의 세기 배율. 낙하 속도로 정해지고 흔들리는 동안 유지된다.
    private float _impactScale;

    private void Awake()
    {
        _rect = (RectTransform)transform;
        _restPosition = _rect.anchoredPosition;
        _restRotation = _rect.localRotation;
    }

    private void OnEnable()
    {
        PlayerMoveSample.OwnerAirborneChanged += HandleOwnerAirborneChanged;
        PlayerMoveSample.OwnerLanded += HandleOwnerLanded;
    }

    private void OnDisable()
    {
        PlayerMoveSample.OwnerAirborneChanged -= HandleOwnerAirborneChanged;
        PlayerMoveSample.OwnerLanded -= HandleOwnerLanded;

        // 진행 중에 꺼지면 어긋난 자리로 남는다. 다시 켤 때 그 자리에서 시작하지 않게 되돌린다.
        _isAirborne = false;
        _liftOffset = 0f;
        _shakeElapsed = -1f;
        Restore();
    }

    private void HandleOwnerAirborneChanged(bool airborne)
    {
        _isAirborne = IsEnabled && airborne;
    }

    private void HandleOwnerLanded(float impactSpeed)
    {
        if (!IsEnabled) return;

        // 살짝 뛰어내린 것까지 흔들면 흔들림이 그냥 장식이 된다. 세게 떨어졌을 때만 반응해야
        // "높은 데서 떨어졌다"는 정보가 된다.
        _impactScale = Mathf.InverseLerp(_minImpactSpeed, _maxImpactSpeed, impactSpeed);

        if (_impactScale <= 0f) return;

        _shakeElapsed = 0f;
        _phaseX = Random.Range(0f, Mathf.PI * 2f);
        _phaseY = Random.Range(0f, Mathf.PI * 2f);
    }

    private void Update()
    {
        // 연출 중에 설정이 꺼지면 어긋난 자리에 굳는다. 한 번 되돌려 놓고 손을 뗀다.
        if (!IsEnabled)
        {
            if (!_isAirborne && _liftOffset <= 0f && _shakeElapsed < 0f) return;

            _isAirborne = false;
            _liftOffset = 0f;
            _shakeElapsed = -1f;
            Restore();
            return;
        }

        // 떠 있는 동안에는 계속 돌아야 한다. 들린 높이를 유지하는 것도 일이다.
        if (!_isAirborne && _liftOffset <= 0f && _shakeElapsed < 0f) return;

        // 들림은 목표를 지수적으로 따라간다. 뜨면 올라가 머물고, 착지하면 흔들림과 함께 내려온다.
        float target = _isAirborne ? _liftStrength : 0f;

        _liftOffset = Mathf.Lerp(_liftOffset, target,
            1f - Mathf.Exp(-_liftFollowSpeed * Time.unscaledDeltaTime));

        // 목표에 충분히 붙으면 딱 맞춰 끝낸다. 지수 접근은 영원히 목표에 닿지 않는다.
        if (Mathf.Abs(_liftOffset - target) < 0.01f)
        {
            _liftOffset = target;
        }

        Vector2 offset = new Vector2(0f, _liftOffset);
        float roll = 0f;

        if (_shakeElapsed >= 0f)
        {
            _shakeElapsed += Time.unscaledDeltaTime;

            if (_shakeElapsed >= _duration)
            {
                _shakeElapsed = -1f;
            }
            else
            {
                // 처음이 가장 크고 빠르게 잦아든다. 선형으로 줄이면 끝까지 흔들리는 것처럼 보인다.
                float remaining = 1f - _shakeElapsed / _duration;
                float decay = remaining * remaining;

                // 가로와 세로에 서로 어긋나는 주기를 줘야 한 방향으로만 튕기는 것처럼 보이지 않는다.
                float x = Mathf.Sin(_shakeElapsed * _frequency + _phaseX);
                float y = Mathf.Sin(_shakeElapsed * _frequency * 1.37f + _phaseY);

                offset += new Vector2(x * _horizontalRatio, y) * (_strength * decay * _impactScale);
                roll += x * _rotationStrength * decay * _impactScale;
            }
        }

        if (!_isAirborne && _liftOffset <= 0f && _shakeElapsed < 0f)
        {
            Restore();
            return;
        }

        _rect.anchoredPosition = _restPosition + offset;
        _rect.localRotation = _restRotation * Quaternion.Euler(0f, 0f, roll);
    }

    private void Restore()
    {
        if (_rect == null) return;

        _rect.anchoredPosition = _restPosition;
        _rect.localRotation = _restRotation;
    }
}
