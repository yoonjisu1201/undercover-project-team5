using TMPro;
using Unity.Netcode;
using UnityEngine;

// [테스트용 임시 스크립트] 범인 NPC 머리 위에 "범인" 라벨을 띄운다.
// CriminalNpcManager.CriminalNpc는 이미 NetworkVariable로 동기화되어 있어서
// 이 스크립트는 네트워크 코드 없이 로컬에서 읽기만 하면 모든 클라이언트에서 동작한다.
// 테스트가 끝나면 이 스크립트와 부착된 GameObject를 삭제하면 된다.
public class CriminalDebugLabel : MonoBehaviour
{
    [SerializeField] private float _heightOffset = 2.2f;
    [SerializeField] private Color _textColor = Color.red;
    [SerializeField] private float _fontSize = 6f;

    private CriminalNpcManager _criminalNpcManager;
    private NetworkObject _labeledCriminal;
    private TextMeshPro _label;
    private Camera _mainCamera;
    private bool _isVisible;

    // 디버그 메뉴에서 범인 머리 위 텍스트의 표시 상태를 변경합니다.
    public void SetVisible(bool visible)
    {
        _isVisible = visible;

        if (_label != null)
        {
            _label.gameObject.SetActive(visible);
        }
    }

    private void Update()
    {
        if (_criminalNpcManager == null)
        {
            _criminalNpcManager = FindFirstObjectByType<CriminalNpcManager>();
            if (_criminalNpcManager == null) return;
        }

        NetworkObject criminal = _criminalNpcManager.CriminalNpc;

        if (criminal != _labeledCriminal)
        {
            _labeledCriminal = criminal;
            AttachLabel(criminal);
        }

        if (_label == null) return;

        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
        }

        if (_mainCamera != null)
        {
            _label.transform.rotation = _mainCamera.transform.rotation;
        }
    }

    private void AttachLabel(NetworkObject criminal)
    {
        if (_label != null)
        {
            Destroy(_label.gameObject);
            _label = null;
        }

        if (criminal == null) return;

        GameObject labelObject = new GameObject("CriminalDebugLabel");
        labelObject.transform.SetParent(criminal.transform, false);
        labelObject.transform.localPosition = Vector3.up * _heightOffset;

        _label = labelObject.AddComponent<TextMeshPro>();
        _label.text = "범인";
        _label.color = _textColor;
        _label.fontSize = _fontSize;
        _label.alignment = TextAlignmentOptions.Center;
        _label.gameObject.SetActive(_isVisible);
    }
}
