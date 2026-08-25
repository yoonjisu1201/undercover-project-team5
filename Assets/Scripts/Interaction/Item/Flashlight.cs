using Unity.Netcode;
using UnityEngine;

// 손전등 오브젝트에 부착. 소유자/논오너 구분 없이 각자 자기 카메라의 시선 방향으로
// Raycast해서 처음 닿는 지점을 바라본다. 방향은 카메라 트랜스폼을 직접 읽지 않고
// PlayerCameraController.ViewPitch(오너/논오너 모두 동기화됨)로 다시 계산한다.
// 맞는 표면이 바뀌면 목표 지점이 순간적으로 튈 수 있어서, 회전은 Slerp로 따라가게 해 흔들림을 완화한다.
// 손 소켓(NetworkObject 아님)엔 파렌팅할 수 없어서, 위치는 매 프레임 handAnchor를 따라간다.
public class Flashlight : NetworkBehaviour
{
    [SerializeField, Min(0f)] private float _maxDistance = 30f;
    [SerializeField, Min(0f)] private float _rotationLerpSpeed = 15f;

    // On/Off 상태는 오너가 직접 토글하는 값이라 Owner 권한으로 쓴다.
    private readonly NetworkVariable<bool> _isOn =
        new(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private PlayerCameraController _playerCameraController;
    private Transform _handAnchor;
    private Renderer[] _renderers;
    // 프리팹 안에 Spot Light, Point Light 두 개가 있어서 배열로 한꺼번에 켜고 끈다.
    private Light[] _lights;
    private bool _isVisible = true;

    public void Initialize(PlayerCameraController playerCameraController, Transform handAnchor)
    {
        _playerCameraController = playerCameraController;
        _handAnchor = handAnchor;
    }

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _lights = GetComponentsInChildren<Light>(true);
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

    private void LateUpdate()
    {
        if (_handAnchor != null)
        {
            transform.position = _handAnchor.position;
        }

        if (_playerCameraController == null)
        {
            return;
        }

        Transform root = _playerCameraController.transform;
        Vector3 origin = _playerCameraController.HeadPivot.transform.position;
        Vector3 direction = Quaternion.AngleAxis(_playerCameraController.ViewPitch, root.right) * root.forward;

        Vector3 targetPoint = Physics.Raycast(origin, direction, out RaycastHit hit, _maxDistance)
            ? hit.point
            : origin + direction * _maxDistance;

        Quaternion targetRotation = Quaternion.LookRotation(targetPoint - transform.position);
        float t = 1f - Mathf.Exp(-_rotationLerpSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, t);
    }
}
