using Unity.Netcode;
using UnityEngine;

// 손전등 오브젝트에 부착. 몸통(메쉬)은 ItemBase와 동일하게 손 회전을 그대로 따라간다. 실제 빛(Light)은
// PlayerCameraController.LightFollowPivot(카메라 시야각을 오너/논오너 모두 그대로 따라가는 피벗)에
// 파렌팅해 raycast 없이도 시선 방향을 공유한다.
// 손 소켓(NetworkObject 아님)엔 파렌팅할 수 없어서, 몸통 위치는 매 프레임 handAnchor를 따라간다.
// Light는 NetworkObject가 아니라 실제 Transform.SetParent로 그 피벗 밑에 붙일 수 있다.
// 손전등은 손 앵커를 LateUpdate에서 따라간다. 1인칭에서는 그 앵커가 카메라의 자식이라,
// 카메라를 옮기는 PlayerCameraController.LateUpdate보다 먼저 돌면 한 프레임씩 뒤처져
// 움직일 때마다 손에서 떨어졌다 붙었다 한다. 실행 순서를 뒤로 밀어 항상 나중에 따라가게 한다.
[DefaultExecutionOrder(100)]
public class Flashlight : NetworkBehaviour
{
    [SerializeField] private Vector3 _holdOffset;

    [Tooltip("캐릭터 손에 들렸을 때의 각도(도). 손 회전에 더한다. x 를 양수로 두면 손전등이 아래를 향한다")]
    [SerializeField] private Vector3 _holdRotationOffset;
    // 빛의 자리는 손전등 몸통 기준이다. 시야 피벗은 플레이어 발바닥에 있어서, 거기에 오프셋으로
    // 매달면 피치가 돌 때마다 빛이 큰 호를 그린다. 위를 보면 뒤로 넘어가 팔에 맞고 아래를 보면
    // 바닥에 처박힌다. 자리는 손전등에 두고 방향만 시야를 따라가게 한다.
    [Tooltip("빛이 나오는 자리. 손전등 렌즈에 둔 앵커를 물린다")]
    [SerializeField] private Transform _lensAnchor;

    [Tooltip("앵커가 비었을 때만 쓰는 예비 자리. 손전등 몸통 기준")]
    [SerializeField] private Vector3 _headLightLocalOffset;

    [Tooltip("시야 방향에서 얼마나 틀어서 비출지(도). x 를 양수로 두면 살짝 아래를 비춘다")]
    [SerializeField] private Vector3 _headLightLocalEuler;


    // 캐릭터 손에 들렸을 때의 자리. 손 로컬 기준이라 손이 돌아도 같이 따라간다.
    // 플레이 중에 눈으로 맞출 수 있도록 열어 둔다.
    public Vector3 HoldOffset {
        get => _holdOffset;
        set => _holdOffset = value;
    }

    // 위치와 마찬가지로 플레이 중에 눈으로 맞출 수 있도록 열어 둔다.
    public Vector3 HoldRotationOffset {
        get => _holdRotationOffset;
        set => _holdRotationOffset = value;
    }

    // On/Off 상태는 오너가 직접 토글하는 값이라 Owner 권한으로 쓴다.
    private readonly NetworkVariable<bool> _isOn =
        new(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private Transform _lightPivot;
    private PlayerCameraController _cameraController;
    private Transform _handAnchor;
    // 1인칭 뷰모델 손의 앵커는 그립 위치를 정확히 가리키고 있어서 _holdOffset을 더하지 않는다.
    // 캐릭터 손 슬롯은 대략적인 위치라 오프셋이 필요하다.
    private bool _handAnchorIsExact;
    private int _defaultLayer;
    private Renderer[] _renderers;
    // 프리팹 안에 Spot Light, Point Light 두 개가 있어서 배열로 한꺼번에 켜고 끈다.
    private Light[] _lights;
    private bool _isVisible = true;

    // 1인칭 뷰모델 손 <-> 캐릭터 손 사이에서 들고 있는 자리를 갈아 끼운다.
    public void SetHandAnchor(Transform handAnchor, bool isExact)
    {
        _handAnchor = handAnchor;
        _handAnchorIsExact = isExact;
    }

    // 내 화면에서만 레이어를 옮긴다. 레이어는 클라이언트마다 따로라서 남의 화면에는 영향이 없고,
    // 손전등은 NetworkTransform이 없어 위치도 각자 잡으므로 서로 간섭하지 않는다.
    public void SetFirstPersonRendering(bool useFirstPerson)
    {
        int layer = useFirstPerson ? Layers.FirstPersonHands : _defaultLayer;
        foreach (Renderer itemRenderer in _renderers)
        {
            itemRenderer.gameObject.layer = layer;
        }
    }

    public void Initialize(PlayerCameraController playerCameraController, Transform handAnchor)
    {
        _handAnchor = handAnchor;

        // 몸통을 따라 움직이지 않도록 손전등 밖으로 빼 둔다. 자리와 방향은 LateUpdate 에서 직접 잡는다.
        _cameraController = playerCameraController;
        _lightPivot = playerCameraController.LightFollowPivot;
        foreach (Light light in _lights)
        {
            light.transform.SetParent(_lightPivot, worldPositionStays: false);
        }
    }

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _lights = GetComponentsInChildren<Light>(true);
        _defaultLayer = gameObject.layer;
    }

    public override void OnNetworkSpawn()
    {
        _isOn.OnValueChanged += HandleIsOnChanged;
        ApplyLightState();
    }

    public override void OnNetworkDespawn()
    {
        _isOn.OnValueChanged -= HandleIsOnChanged;
    }

    public void ToggleOnOff()
    {
        if (!IsOwner)
        {
            return;
        }

        _isOn.Value = !_isOn.Value;
    }

    private void HandleIsOnChanged(bool previousValue, bool newValue)
    {
        ApplyLightState();
    }

    // 카트를 드는 등 손이 다른 데 쓰일 때 시각적으로 숨긴다.
    // NetworkObject라 GameObject.SetActive 대신 렌더러/라이트만 끈다(ItemBase와 동일한 이유).
    public void SetVisible(bool isVisible)
    {
        _isVisible = isVisible;
        foreach (Renderer itemRenderer in _renderers)
        {
            itemRenderer.enabled = isVisible;
        }
        ApplyLightState();
    }

    private void ApplyLightState()
    {
        bool shouldBeOn = _isOn.Value && _isVisible;
        foreach (Light light in _lights)
        {
            light.enabled = shouldBeOn;
        }
    }

    // 자리는 손전등, 방향은 시야. 손전등 몸통이 어디에 있든 빛은 항상 손전등에서 나가고,
    // 회전 중심이 빛과 같은 자리라 시야를 올리고 내려도 빛이 휩쓸리지 않는다.
    private void ApplyLightPose()
    {
        if (_cameraController == null)
        {
            return;
        }

        // 피벗을 그대로 쓰지 않고 yaw 와 pitch 를 다시 조립한다. 위쪽만 묶어야 하기 때문이다.
        //
        // 상한은 팔이 멈추는 각도를 그대로 쓴다. 천장을 올려다볼 때 눈은 더 올라가도 팔은
        // 거기서 멈추는데, 빔만 계속 따라 올라가면 손전등은 앞을 보는데 빛은 위로 새어 나간다.
        // 아래로는 묶지 않는다. 발밑을 살피는 건 손전등의 정상적인 쓰임이다.
        float pitch = Mathf.Max(_cameraController.ViewPitch, _cameraController.ArmFollowMinPitch);
        Quaternion rotation = _cameraController.ViewYawRotation
            * Quaternion.Euler(pitch, 0f, 0f)
            * Quaternion.Euler(_headLightLocalEuler);
        Vector3 position = _lensAnchor != null
            ? _lensAnchor.position
            : transform.TransformPoint(_headLightLocalOffset);
        foreach (Light light in _lights)
        {
            light.transform.SetPositionAndRotation(position, rotation);
        }
    }

    private void LateUpdate()
    {
        if (_handAnchor == null)
        {
            return;
        }

        // 1인칭 뷰모델 앵커는 그립 자세를 정확히 가리키고 있어서 손대지 않는다.
        // 캐릭터 손은 대략적인 자리라 위치도 각도도 보정이 필요하다.
        if (_handAnchorIsExact)
        {
            transform.SetPositionAndRotation(_handAnchor.position, _handAnchor.rotation);
            ApplyLightPose();
            return;
        }

        transform.SetPositionAndRotation(
            _handAnchor.TransformPoint(_holdOffset),
            _handAnchor.rotation * Quaternion.Euler(_holdRotationOffset));

        ApplyLightPose();
    }
}
