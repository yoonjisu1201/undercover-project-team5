using UnityEngine;
using Unity.Netcode;
using System;

// 투표 패널에 표시할 검거 후보 NPC의 실시간 초상 이미지를 렌더링한다.
// 후보 정면 고정 오프셋으로 카메라를 옮겨 RenderTexture에 그리는 방식
public class ArrestCandidatePortrait : MonoBehaviour
{
    [SerializeField] private Camera _portraitCamera;
    [SerializeField] private float _distance = 1.2f;
    [SerializeField] private float _heightOffset = 1.5f;
    [SerializeField] private float _downwardTiltDegrees = 8f; // 카메라가 살짝 위에서 내려다보도록 추가하는 다운틸트 각도
    [SerializeField] private string _portraitOnlyLayerName = "NpcPortraitOnly"; // 캡처 순간에만 후보를 이 레이어로 바꿔서 다른 NPC를 가린다.

    private int _portraitOnlyLayer;
    private Transform[] _overriddenParts;
    private int _originalLayer; // 모든 하위 파츠가 같은 레이어를 쓰므로 하나만 기억해도 충분하다
    private GameObject _portraitBackdrop;

    private void Awake()
    {
        if (_portraitCamera != null)
        {
            _portraitCamera.enabled = false; // Render()로 필요할 때만 수동 캡처한다
        }

        Transform backdropTransform = transform.Find("Quad");
        if (backdropTransform != null)
        {
            _portraitBackdrop = backdropTransform.gameObject;
            _portraitBackdrop.SetActive(false);
        }

        _portraitOnlyLayer = LayerMask.NameToLayer(_portraitOnlyLayerName);
        if (_portraitOnlyLayer < 0)
        {
            Debug.LogError($"[ArrestCandidatePortrait] '{_portraitOnlyLayerName}' 레이어가 없습니다. Project Settings에서 추가해주세요.");
        }
    }

    public void ShowCandidate(NetworkObject candidate)
    {
        if (_portraitCamera == null) return;
        if (candidate == null) return;
        if (_portraitOnlyLayer < 0) return;

        Transform candidateTransform = candidate.transform;
        _portraitCamera.transform.position = candidateTransform.position
            + candidateTransform.forward * _distance
            + Vector3.up * _heightOffset;
        _portraitCamera.transform.LookAt(candidateTransform.position
            + Vector3.up * _heightOffset);
        _portraitCamera.transform.Rotate(_downwardTiltDegrees, 0f, 0f, Space.Self); // 정면 수평 시선에서 살짝 아래로 기울인다

        // 위장이 해제된 범인은 본모습으로 찍히므로, 캡처하는 이 한 프레임만 시민 외형으로 되돌린다.
        // (해제 전이라면 두 호출 모두 아무것도 하지 않아 투표 패널 경로는 그대로다)
        candidate.TryGetComponent(out CriminalAlienReveal alienReveal);
        alienReveal?.BeginHumanFormCapture();

        OverrideLayer(candidateTransform); // 되살린 시민 파츠까지 포함해야 하므로 순서가 중요하다
        _portraitBackdrop?.SetActive(true);

        try
        {
            _portraitCamera.Render(); // 후보만 보이는 레이어로 바꾼 상태에서 한 프레임만 캡처
        }
        finally
        {
            _portraitBackdrop?.SetActive(false);
            RestoreLayer();
            alienReveal?.EndHumanFormCapture();
        }
    }

    // 후보 NPC의 활성화된 파츠 전체를 전용 레이어로 바꿔서, 캡처 순간 다른 NPC가 같이 찍히지 않게 한다.
    private void OverrideLayer(Transform candidateTransform)
    {
        _overriddenParts = candidateTransform.GetComponentsInChildren<Transform>(); // 비활성화된 파츠는 어차피 안 보이므로 제외
        _originalLayer = candidateTransform.gameObject.layer;

        foreach (Transform part in _overriddenParts)
        {
            part.gameObject.layer = _portraitOnlyLayer;
        }
    }

    // 캡처가 끝나면 원래 레이어로 되돌려서 평소 상호작용/충돌 판정에 영향을 주지 않게 한다.
    private void RestoreLayer()
    {
        foreach (Transform part in _overriddenParts)
        {
            if (part != null)
            {
                part.gameObject.layer = _originalLayer;
            }
        }

        _overriddenParts = null;
    }

    //확인 패널에서 [아니요]를 눌러 투표를 시작하지 않을 때, 캡처해둔 이미지를 지운다.
    public void Clear()
    {
        if (_portraitCamera == null) return;

        RenderTexture renderTexture = _portraitCamera.targetTexture;
        if (renderTexture == null) return;

        RenderTexture previousActive = RenderTexture.active;
        RenderTexture.active = renderTexture;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = previousActive;
    }
}
