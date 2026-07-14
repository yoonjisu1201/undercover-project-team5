using DG.Tweening;
using EPOOutline;
using Unity.Netcode;
using UnityEngine;

public class PickupItem : NetworkBehaviour, IInteractable
{
    [SerializeField] private ItemData _itemData;

    [Header("외곽선 설정")]
    [SerializeField] private Outlinable _outlinable;
    [SerializeField, Min(0f)] private float _outlineFadeDuration = 0.15f;   // 외곽선 페이드 지속 시간

    private Tween _outlineTween;
    private Color _outlineColor;    // 외곽선 색상 저장
    private Color _frontOutlineColor;   // 전면 외곽선 색상 저장
    private Color _backOutlineColor;    // 후면 외곽선 색상 저장
    private float _outlineVisibility;   // 외곽선 투명도
    private float _interactionBlockedUntil;

    public string InteractionText => _itemData != null ? $"{_itemData.DisplayName} 줍기" : "앉기";
    public bool CanInteract => Time.time >= _interactionBlockedUntil;   // 상호작용 가능 여부

    private void Awake()
    {
        if (_outlinable == null)
        {
            _outlinable = GetComponentInChildren<Outlinable>();
        }

        if (_outlinable == null)
        {
            return;
        }

        _outlineColor = _outlinable.OutlineParameters.Color;    // 외곽선 색상 저장
        _frontOutlineColor = _outlinable.FrontParameters.Color; // 전면 외곽선 색상 저장
        _backOutlineColor = _outlinable.BackParameters.Color;   // 후면 외곽선 색상 저장

        _outlineVisibility = 0f;    // 초기 외곽선 가시성은 0으로 설정
        ApplyOutlineVisibility(_outlineVisibility);
        _outlinable.enabled = false;
    }

    public void SetOutline(bool isVisible)
    {
        if (_outlinable == null)
        {
            return;
        }

        _outlineTween?.Kill();  // 이전 트윈이 존재하면 종료

        if (isVisible)
        {
            _outlinable.enabled = true;
        }

        float targetVisibility = isVisible ? 1f : 0f;   // 목표 외곽선 가시성 설정

        if (_outlineFadeDuration <= 0f) // 외곽선 페이드 지속 시간이 0 이하이면 즉시 적용
        {
            _outlineVisibility = targetVisibility;
            ApplyOutlineVisibility(_outlineVisibility);
            _outlinable.enabled = isVisible;
            return;
        }

        _outlineTween = DOTween.To(() => _outlineVisibility, value =>   // 외곽선 가시성 업데이트
                {
                    _outlineVisibility = value;
                    ApplyOutlineVisibility(value);
                },
                targetVisibility, _outlineFadeDuration).SetEase(Ease.OutQuad).SetTarget(this);

        if (!isVisible) // 외곽선이 사라진 후 Outlinable을 비활성화
        {
            _outlineTween.OnComplete(() => _outlinable.enabled = false);
        }
    }

    public void BlockInteraction(float duration)    // duration초 동안 상호작용 차단
    {
        _interactionBlockedUntil = Time.time + Mathf.Max(0f, duration);
        SetOutline(false);
    }

    public void Interact(GameObject interactor)
    {
        if (!CanInteract || _itemData == null)
        {
            return;
        }

        if (!IsSpawned)
        {
            Debug.LogWarning($"'{name}'이 NetworkObject로 스폰되지 않았습니다.");
            return;
        }

        RequestPickupRpc();
    }

    //--- 외곽선 색, Alpha 값 적용 ---//
    private void ApplyOutlineVisibility(float visibility)
    {
        Color outlineColor = _outlineColor;
        outlineColor.a *= visibility;
        _outlinable.OutlineParameters.Color = outlineColor;

        Color frontOutlineColor = _frontOutlineColor;
        frontOutlineColor.a *= visibility;
        _outlinable.FrontParameters.Color = frontOutlineColor;

        Color backOutlineColor = _backOutlineColor;
        backOutlineColor.a *= visibility;
        _outlinable.BackParameters.Color = backOutlineColor;
    }

    public override void OnDestroy()
    {
        _outlineTween?.Kill();
        base.OnDestroy();
    }

    //--- 서버에서 아이템 줍기 요청 처리 Rpc 관련 코드 ---//
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestPickupRpc(RpcParams rpcParams = default)
    {
        if (!IsSpawned || _itemData == null)
        {
            return;
        }

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient senderClient))
        {
            return;
        }

        NetworkObject playerObject = senderClient.PlayerObject;

        if (playerObject == null)
        {
            return;
        }

        SphereCollider interactionCollider = playerObject.GetComponent<SphereCollider>();

        if (interactionCollider == null)
        {
            return;
        }

        if (!IsOverlappingInteractionCollider(interactionCollider))
        {
            return;
        }

        PlayerInventory inventory = playerObject.GetComponent<PlayerInventory>();

        if (inventory == null)
        {
            return;
        }

        // 서버에서 인벤토리 공간을 확인하고 아이템을 추가
        if (!inventory.TryAddItemOnServer(_itemData.ItemId))
        {
            return;
        }

        // 모든 클라이언트에서 아이템 제거
        NetworkObject.Despawn();
    }

    private bool IsOverlappingInteractionCollider(SphereCollider interactionCollider)
    {
        Collider[] itemColliders = GetComponentsInChildren<Collider>();

        foreach (Collider itemCollider in itemColliders)
        {
            if (!itemCollider.enabled || itemCollider == interactionCollider)
            {
                continue;
            }

            if (Physics.ComputePenetration(
                    interactionCollider,
                    interactionCollider.transform.position,
                    interactionCollider.transform.rotation,
                    itemCollider,
                    itemCollider.transform.position,
                    itemCollider.transform.rotation,
                    out _,
                    out _))
            {
                return true;
            }
        }

        return false;
    }
}
