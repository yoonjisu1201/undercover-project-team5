using UnityEngine;
using UnityEngine.UI;

// 피격 화면 연출. 피격 순간에는 방향 표시만 띄우고, 붉은 혈흔은 저체력 상태에서만 상시 표시한다.
// 두 연출의 역할이 다르다 — 방향 표시는 '지금 어디서 맞았나'라는 사건이고,
// 혈흔은 '지금 위험하다'는 상태다. 그래서 피격 강도에 따라 혈흔이 번쩍이지는 않는다. (#469)
//
// 혈흔에 URP Vignette를 쓰지 않는 이유(#469에서 실제로 시도했다): Vignette는 화면을 비네트 색으로
// '곱하는' 효과라 어둡게 만드는 것만 가능하고 붉은색을 더할 수 없다. 이 게임의 야간 맵은 화면
// 가장자리가 이미 거의 검은색이어서, 검은 픽셀에 어두운 빨강을 곱해도 그대로 검은색이라 아무것도
// 보이지 않는다. 그래서 색을 더하는 UI 오버레이로 처리한다.
//
// 순수 표현 컴포넌트로 유지한다. "누가 언제 맞았나"는 PlayerHealth가 알고 여기는 화면의 일만 한다.
// 로컬 연출이므로 네트워크와 무관하다 — 피격 사실은 PlayerHealth가 오너에게 보내는 RPC로 들어온다.
public sealed class HitScreenEffect : MonoBehaviour
{
    // 피격 RPC를 받은 PlayerHealth가 찾아올 수 있게 노출한다. 화면은 하나뿐이므로 인스턴스도 하나다.
    public static HitScreenEffect Instance { get; private set; }

    [Header("피격 방향 표시")]
    [Tooltip("화면 중앙을 축으로 회전시킬 부채꼴 이미지(T_HitDirectionArc). 기본 회전에서 화면 위쪽을 가리키도록 배치할 것")]
    [SerializeField] private RectTransform _directionArc;
    [Tooltip("방향 표시가 유지되는 시간(초)")]
    [SerializeField] private float _arcSeconds = 0.8f;
    [SerializeField, Range(0f, 1f)] private float _arcMaxAlpha = 0.85f;


    [Header("저체력 혈흔")]
    [Tooltip("화면을 덮는 오버레이 이미지(T_BloodVignette). 알파를 코드가 구동하므로 씬에서는 알파 0으로 둬도 된다")]
    [SerializeField] private Image _bloodOverlay;
    [SerializeField] private Color _bloodColor = new Color(0.55f, 0.03f, 0.03f);
    [SerializeField, Range(0f, 1f)] private float _lowHealthThreshold = 0.3f;   // 이 비율 이하로 체력이 떨어지면 URP 비네트 혈은이 표시됨
    [SerializeField, Range(0f, 1f)] private float _lowHealthMinAlpha = 0f;  // 비네트 최소 알파값
    [SerializeField, Range(0f, 1f)] private float _lowHealthMaxAlpha = 0.9f;    // 비네트 최대 알파값
    [SerializeField] private float _pulseSeconds = 2.3f;    // 펄스가 적용된 최종 알파 비율 Min * 0.6 ~ Max * 1.0 

    private const float PulseFloor = 0.6f;  // 펄스 최저 비율

    private float _arcElapsed = -1f;    // 음수 = 진행 중 아님

    private Image _arcImage;    // _directionArc의 Image 컴포넌트. Awake에서 TryGetComponent로 가져온다.

    // 저체력 판정에 쓸 로컬 플레이어의 체력. Player는 씬 로드보다 늦게 스폰되므로 지연 해석한다.
    private PlayerHealth _localHealth;

    private void Awake()
    {
        Instance = this;

        if (_directionArc != null)
        {
            _directionArc.TryGetComponent(out _arcImage);
        }

        if (_bloodOverlay == null)
        {
            Debug.LogError("[HitScreenEffect] 혈흔 오버레이 이미지가 연결되지 않아 저체력 연출을 표시할 수 없습니다.", this);
        }
        else
        {
            // 오버레이가 클릭을 막지 않게 한다. 씬 설정에 의존하지 않고 코드에서 보장한다.
            _bloodOverlay.raycastTarget = false;
        }

        ApplyBloodAlpha(0f);
        SetArcAlpha(0f);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // 피격 연출 — 가해자 방향을 화면에 표시한다.
    // sourcePosition: 가해자의 월드 위치. 화면 각도 환산은 로컬 카메라를 아는 이쪽에서 끝낸다.
    public void PlayHit(Vector3 sourcePosition)
    {
        if (_directionArc == null)
        {
            return;
        }

        Camera camera = LocalCameraProvider.MainCamera;
        if (camera == null)
        {
            return;
        }

        // 카메라 로컬 기준으로 환산한다. 정면이 0도, 오른쪽이 +90도, 뒤가 ±180도다.
        Vector3 localDirection = camera.transform.InverseTransformDirection(sourcePosition - camera.transform.position);
        float angle = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;

        // UI의 +Z 회전은 반시계인데 위 각도는 시계 방향이 양수라 부호를 뒤집는다.
        _directionArc.localRotation = Quaternion.Euler(0f, 0f, -angle);
        _arcElapsed = 0f;   // 피격 방향 표시 진행 시작
    }

    private void Update()
    {
        TickArc();
        ApplyBloodAlpha(CurrentLowHealthAlpha());
    }

    private void TickArc()  // 피격 방향 표시의 진행을 갱신한다. Update에서 매 프레임 호출된다.
    {
        if (_arcElapsed < 0f)   // 음수일 때 피격 방향 표시 진행 안함
        {
            return;
        }

        _arcElapsed += Time.deltaTime;  // 진행 시간 누적
        if (_arcElapsed >= _arcSeconds) // 진행 시간 초과 시 표시 종료
        {
            _arcElapsed = -1f;
            SetArcAlpha(0f);
            return;
        }

        // 방향 표시는 유지 시간 동안 고정 알파로 보여준다.
        SetArcAlpha(_arcMaxAlpha);
    }

    private float CurrentLowHealthAlpha()
    {
        if (!TryGetLocalHealth(out PlayerHealth health))
        {
            return 0f;
        }

        // 다운 중에는 끈다. HP 0이면 어차피 비율이 임계값 아래라, 이 게이트가 없으면 쓰러진 내내 맥동한다.
        if (health.IsDowned || health.MaxHp <= 0f)
        {
            return 0f;
        }

        float ratio = health.CurrentHp / health.MaxHp;
        if (ratio > _lowHealthThreshold)
        {
            return 0f;
        }

        // 임계값에 걸친 순간부터 곧바로 보여야 하므로 최소 알파를 바닥으로 깔고, 체력이 더 깎일수록 진해진다.
        // 알파 전체를 남은 체력에 비례해 줄이면 임계값 근처에서 0에 수렴해 아무것도 보이지 않는다.
        float severity = _lowHealthThreshold <= 0f ? 1f : Mathf.Clamp01(1f - ratio / _lowHealthThreshold);
        float peak = Mathf.Lerp(_lowHealthMinAlpha, _lowHealthMaxAlpha, severity);

        if (_pulseSeconds <= 0f)
        {
            return peak;
        }

        // 맥동은 0까지 내려가지 않고 최고 알파의 PulseFloor~100% 사이에서만 흔들린다.
        // 0까지 떨어뜨리면 맥동 사이마다 연출이 완전히 사라져 저체력이라는 '상태'로 읽히지 않는다.
        float pulse = 0.5f - 0.5f * Mathf.Cos(Time.time / _pulseSeconds * 2f * Mathf.PI);

        return peak * Mathf.Lerp(PulseFloor, 1f, pulse);
    }

    // 로컬(오너) 플레이어의 체력을 찾는다. 아직 스폰되지 않았으면 다음 프레임에 다시 시도한다.
    private bool TryGetLocalHealth(out PlayerHealth health)
    {
        // 라운드 종료로 디스폰되면 파괴된 참조가 남으므로 매번 살아있는지 확인한다.
        if (_localHealth != null)
        {
            health = _localHealth;
            return true;
        }

        foreach (Player player in Player.ActiveInstances)
        {
            if (player.IsOwner)
            {
                _localHealth = player.PlayerHealth;
                health = _localHealth;
                return true;
            }
        }

        health = null;
        return false;
    }

    private void ApplyBloodAlpha(float alpha)
    {
        if (_bloodOverlay == null)
        {
            return;
        }

        Color color = _bloodColor;
        color.a = Mathf.Clamp01(alpha);
        _bloodOverlay.color = color;
    }

    private void SetArcAlpha(float alpha)
    {
        if (_arcImage == null)
        {
            return;
        }

        Color color = _arcImage.color;
        color.a = alpha;
        _arcImage.color = color;
    }
}
