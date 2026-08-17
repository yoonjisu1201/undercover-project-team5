using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 검거 추격 단계의 게이지 바와 "왜 게이지가 안 오르는지" 안내 문구를 보여주는 UI.
// 패널 표시/게이지 값은 ArrestChaseManager의 이벤트를 구독해서 갱신하고,
// 안내 문구만 추격 중(Chasing)일 때 매 프레임 로컬 플레이어의 위치/인벤토리를 확인해서 갱신한다.
// (범위 안/밖, 도구 장착 여부는 "계속 변하는 값"이라 이벤트로는 알 수 없어 폴링이 불가피하다)
public class ArrestChaseUI : MonoBehaviour
{
    [SerializeField] private GameObject _chasePanel;  // 추격 중(+완료 순간)에만 보여줄 패널
    [SerializeField] private Slider _gaugeSlider;      // 검거 게이지 바
    [SerializeField] private TMP_Text _gaugeText;      // "검거 진행 68%" 같은 퍼센트 텍스트

    [SerializeField] private Image[] _participantIcons;                        // 참여 인원 아이콘 2개
    [SerializeField] private Color _participantActiveColor = Color.yellow;     // 단축키 홀드 중인 인원 수만큼 앞에서부터 이 색으로 바뀐다. 기본은 흰색.
    [SerializeField] private TMP_Text _participantCountText;                   // "0/2" 같은 홀드 인원 수 텍스트

    [SerializeField] private TMP_Text _distanceText;   // 로컬 플레이어와 타겟 NPC 사이 거리 ("12m" 등)

    [SerializeField] private GameObject _promptPanel;  // "도구가 필요합니다" 등 안내 문구 패널
    [SerializeField] private TMP_Text _promptText;

    // _promptPanel(경고문구) 의 "범위 안에 있는지" 판정에만 쓰는 여유 반경. 실제 검거 반경(5m)보다 크게 잡아서
    // 5m 경계를 막 통과하는 순간 서버의 HoldingCount가 아직 갱신 전이라 문구가 잘못 표시되는 걸 막는다.
    [SerializeField] private float _promptVisibilityRadius = 10f;

    [SerializeField] private float _promptShowDelay = 0.25f; // 서버 HoldingCount 반영 지연으로 인한 순간적인 오탐을 무시하는 유예시간

    private string _pendingPromptMessage;
    private float _pendingPromptSince;

    [SerializeField] private RectTransform _targetingFrame;        // UI_TargetReticle. _chasePanel의 자식이라 표시/숨김은 부모가 처리, 여기서는 위치와 크기만 갱신한다.
    [SerializeField] private float _targetingFramePadding = 1f;  // 대상의 몸 크기보다 얼마나 여유 있게 감쌀지
    [SerializeField] private float _targetingMinScale = 0.5f;
    [SerializeField] private float _targetingMaxScale = 2f;

    private Canvas _canvas; // 프레임의 화면상 픽셀 크기를 구하려면 최상단 캔버스의 해상도 배율이 필요하다

    private void Start()
    {
        _chasePanel.SetActive(false);
        _promptPanel.SetActive(false);

        // 중첩 캔버스가 생겨도 해상도 배율을 가진 최상단 캔버스를 잡도록 rootCanvas까지 따라간다.
        _canvas = _targetingFrame.GetComponentInParent<Canvas>().rootCanvas;

        ArrestChaseManager.Instance.OnStateChanged += HandleStateChanged;
        ArrestChaseManager.Instance.OnGaugeChanged += HandleGaugeChanged;

        // 늦참 클라이언트가 추격 도중 스폰되는 경우, OnValueChanged는 최초 동기화 값에는 발동하지 않으므로
        // 직접 한 번 호출해서 현재 상태를 반영해줘야 한다. (RoundManager.OnRoundStateChanged와 동일한 이유)
        HandleStateChanged(ArrestChaseManager.Instance.CurrentState);
        HandleGaugeChanged(ArrestChaseManager.Instance.Gauge);
    }

    private void OnDestroy()
    {
        if (ArrestChaseManager.Instance != null)
        {
            ArrestChaseManager.Instance.OnStateChanged -= HandleStateChanged;
            ArrestChaseManager.Instance.OnGaugeChanged -= HandleGaugeChanged;
        }
    }

    // 카메라가 LateUpdate에서 움직이므로, 그보다 먼저 계산하면 타겟팅 프레임이 한 프레임씩 어긋나 떨린다.
    private void LateUpdate()
    {
        // 안내 문구는 "지금 이 순간" 로컬 플레이어의 위치/장착 상태에 달려있어서 이벤트로 알 수 없다.
        // 추격 중일 때만 계산하면 되므로 그 외에는 아무것도 하지 않는다.
        if (ArrestChaseManager.Instance == null || ArrestChaseManager.Instance.CurrentState != ArrestChaseState.Chasing)
        {
            return;
        }

        UpdateChasePanelVisibility();
        UpdatePrompt();
        UpdateTargetingFrame();
        UpdateParticipantIcons();
    }

    private void HandleStateChanged(ArrestChaseState state)
    {
        // 표시 여부는 Update()의 UpdateChasePanelVisibility()가 반경 진입 여부로 매 프레임 판단한다.
        // 여기서는 추격이 끝나거나 리셋될 때(Idle) 패널을 확실히 꺼주기만 한다.
        if (state != ArrestChaseState.Chasing && state != ArrestChaseState.Completed)
        {
            _chasePanel.SetActive(false);
        }

        // 추격이 끝나거나 리셋되면 안내 문구도 같이 지운다.
        if (state != ArrestChaseState.Chasing)
        {
            _promptPanel.SetActive(false);
        }
    }

    // 로컬 플레이어가 대상 NPC의 검거 반경 안에 들어왔을 때만 ChasePanel(게이지 바 + 타겟팅 프레임)을 보여준다.
    // (거리를 여기서 이미 계산하므로, 같은 값을 거리 텍스트 갱신에도 그대로 쓴다)
    private void UpdateChasePanelVisibility()
    {
        NetworkObject target = ArrestChaseManager.Instance.Target;
        NetworkObject localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;

        if (target == null || localPlayer == null)
        {
            _chasePanel.SetActive(false);
            return;
        }

        float distance = Vector3.Distance(localPlayer.transform.position, target.transform.position);
        _chasePanel.SetActive(distance <= ArrestChaseManager.Instance.CaptureRadius);
        _distanceText.text = $"{Mathf.RoundToInt(distance)}m";
    }

    // 단축키(ArrestTool, 마우스 좌클릭)를 홀드 중인 인원 수만큼 앞에서부터 아이콘을 노란색으로, 나머지는 흰색으로 표시한다.
    private void UpdateParticipantIcons()
    {
        int holdingCount = ArrestChaseManager.Instance.HoldingCount;
        for (int i = 0; i < _participantIcons.Length; i++)
        {
            _participantIcons[i].color = i < holdingCount ? _participantActiveColor : Color.white;
        }
        _participantCountText.text = $"{holdingCount}/{ArrestChaseManager.Instance.CurrentRequiredParticipants}";
    }

    // 대상 NPC의 렌더러 바운드를 화면 좌표로 변환해 타겟팅 프레임(UI_TargetReticle)의 위치와 크기를 갱신한다.
    // 위장이 풀리면 대상 모델이 통째로 바뀌므로, 고정 오프셋 대신 그때그때의 실제 크기를 따라간다.
    private void UpdateTargetingFrame()
    {
        NetworkObject target = ArrestChaseManager.Instance.Target;
        if (target == null)
        {
            Debug.LogWarning("[ArrestChaseUI] 추격 대상 NPC가 없어 타겟팅 프레임을 갱신하지 못했습니다.");
            return;
        }

        // 화면 좌표는 내 카메라 기준이어야 한다. Camera.main은 CCTV·초상화 카메라를 잡을 수 있다.
        Camera camera = LocalCameraProvider.MainCamera;
        Renderer targetRenderer = FindVisibleRenderer(target);

        if (camera == null || targetRenderer == null)
        {
            _targetingFrame.gameObject.SetActive(false);
            return;
        }

        // 스킨 메시의 bounds는 팔다리가 흔들릴 때마다 매 프레임 커졌다 작아져 프레임이 떤다.
        // 애니메이션과 무관한 원본 바운드를 쓰면 크기가 고정되고, 위치만 대상을 따라간다.
        Bounds local = targetRenderer.localBounds;
        float worldHalfHeight = local.extents.y * targetRenderer.transform.lossyScale.y;

        // localBounds는 루트 본 기준이라 그대로 변환하면 중심이 발치로 내려간다.
        // NPC 루트가 발밑이므로, 거기서 몸 높이의 절반만큼 올리면 몸통 중앙이다.
        Vector3 worldCenter = target.transform.position + Vector3.up * worldHalfHeight;

        Vector3 screenCenter = camera.WorldToScreenPoint(worldCenter);

        // 대상이 카메라 뒤에 있으면(z<0) WorldToScreenPoint가 반대편 좌표를 반환하므로 프레임을 숨긴다.
        bool isBehindCamera = screenCenter.z < 0f;
        _targetingFrame.gameObject.SetActive(!isBehindCamera);
        if (isBehindCamera) return;

        _targetingFrame.position = screenCenter;

        // 몸의 위아래 끝을 함께 투영해 화면에서 차지하는 세로 픽셀 크기를 재고, 그 비율만큼 프레임을 키운다.
        // (원근 때문에 중심에서 위/아래까지의 픽셀 거리가 달라, 한쪽만 재서 두 배 하면 어긋난다)
        Vector3 screenTop = camera.WorldToScreenPoint(worldCenter + Vector3.up * worldHalfHeight);
        Vector3 screenBottom = camera.WorldToScreenPoint(worldCenter - Vector3.up * worldHalfHeight);
        float targetPixelHeight = Mathf.Abs(screenTop.y - screenBottom.y);
        float framePixelHeight = _targetingFrame.rect.height * _canvas.scaleFactor;

        _targetingFrame.localScale = Vector3.one * Mathf.Clamp(
            targetPixelHeight * _targetingFramePadding / framePixelHeight,
            _targetingMinScale,
            _targetingMaxScale);
    }

    // 추격 대상은 위장이 풀린 범인이라 시민 파츠는 전부 꺼져 있다. 켜져 있는 스킨 메시가 곧 외계인 몸이다.
    // (디버그 라벨이나 소품 같은 MeshRenderer가 먼저 잡히지 않도록 스킨 메시만 본다)
    private static Renderer FindVisibleRenderer(NetworkObject target)
    {
        foreach (SkinnedMeshRenderer renderer in target.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (renderer.enabled && renderer.gameObject.activeInHierarchy) return renderer;
        }

        return null;
    }

    private void HandleGaugeChanged(float gauge)
    {
        _gaugeSlider.value = gauge;
        _gaugeText.text = $"{Mathf.RoundToInt(gauge * 100f)}%";
    }

    // "지금 내가(로컬 플레이어) 왜 게이지에 기여하지 못하고 있는지"를 판단해서 안내 문구를 띄운다.
    // 서버 계산과 별개로, 로컬 플레이어 본인의 위치/인벤토리는 클라이언트가 이미 알고 있으므로 여기서 직접 계산한다.
    private void UpdatePrompt()
    {
        NetworkObject target = ArrestChaseManager.Instance.Target;
        NetworkObject localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;

        if (target == null || localPlayer == null)
        {
            Debug.LogWarning("[ArrestChaseUI] 추격 대상 NPC 또는 로컬 플레이어를 찾을 수 없어 안내 문구를 갱신하지 못했습니다.");
            _promptPanel.SetActive(false);
            return;
        }

        float distance = Vector3.Distance(localPlayer.transform.position, target.transform.position);
        bool isInRange = distance <= _promptVisibilityRadius;

        // 범위 밖이면 아직 참여할 상황이 아니므로 안내할 게 없다.
        if (!isInRange)
        {
            _promptPanel.SetActive(false);
            _pendingPromptMessage = null;
            return;
        }

        // R키를 누르고 있지 않으면 아직 시도한 게 아니므로 안내할 게 없다.
        bool isHoldingArrestKey = localPlayer.TryGetComponent(out PlayerArrestInput arrestInput)
            && arrestInput.IsHoldingArrestKey;

        if (!isHoldingArrestKey)
        {
            _promptPanel.SetActive(false);
            _pendingPromptMessage = null;
            return;
        }

        bool hasToolEquipped = localPlayer.TryGetComponent(out PlayerInventory inventory)
            && inventory.TryGetSelectedItemId(out ItemType itemId)
            && itemId == ArrestChaseManager.CaptureToolItemId;

        string desiredMessage = !hasToolEquipped
            ? "검거 도구가 필요합니다"
            : ArrestChaseManager.Instance.HoldingCount < ArrestChaseManager.Instance.CurrentRequiredParticipants
                ? "다른 요원 1명이 필요합니다"
                : null;

        if (desiredMessage == null)
        {
            _promptPanel.SetActive(false); // 조건을 다 채웠으면 안내 없이 게이지 바만 보여준다.
            _pendingPromptMessage = null;
            return;
        }

        // 서버의 HoldingCount가 아직 반영되지 않아 생기는 순간적인 오탐을 걸러내기 위해,
        // 같은 메시지가 _promptShowDelay 이상 지속될 때만 실제로 띄운다.
        if (desiredMessage != _pendingPromptMessage)
        {
            _pendingPromptMessage = desiredMessage;
            _pendingPromptSince = Time.time;
        }

        if (Time.time - _pendingPromptSince >= _promptShowDelay)
        {
            ShowPrompt(desiredMessage);
        }
        else
        {
            _promptPanel.SetActive(false);
        }
    }

    private void ShowPrompt(string text)
    {
        _promptPanel.SetActive(true);
        _promptText.text = text;
    }
}
