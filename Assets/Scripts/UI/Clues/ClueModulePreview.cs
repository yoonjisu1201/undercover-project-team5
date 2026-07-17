using UnityEngine;
using UnityEngine.UI;

public class ClueModulePreview : MonoBehaviour
{
    [Header("Example Module")]
    [SerializeField] private GameObject _modulePrefab;

    [Header("Preview")]
    [SerializeField] private Transform _moduleSpawnPoint;
    [SerializeField] private Camera _clueCamera;
    [SerializeField] private RenderTexture _renderTexture;
    [SerializeField] private RawImage _clueImage;

    private GameObject _createdModule;

    private void Start()
    {
        ShowModule();
    }

    public void ShowModule()
    {
        if (_createdModule != null)
        {
            Destroy(_createdModule);
        }

        _createdModule = Instantiate(
            _modulePrefab,
            _moduleSpawnPoint.position,
            _moduleSpawnPoint.rotation,
            _moduleSpawnPoint
        );

        _createdModule.transform.localPosition = Vector3.zero;
        _createdModule.transform.localRotation = Quaternion.identity;

        Renderer moduleRenderer = _createdModule.GetComponentInChildren<Renderer>();
        if (moduleRenderer != null)
        {
            _createdModule.transform.position += _moduleSpawnPoint.position - moduleRenderer.bounds.center;
        }

        _clueImage.texture = _renderTexture;

        Transform background = _clueImage.transform.Find("BackGround");
        if (background != null)
        {
            background.SetParent(_clueImage.transform.parent, false);
            background.SetAsFirstSibling();
            background.gameObject.SetActive(true);
        }

        _clueImage.GetComponentInParent<ClueUI>(true)?.ShowClueImage(_renderTexture, "확대 이미지 단서");
        _clueCamera.targetTexture = _renderTexture;
        _clueCamera.clearFlags = CameraClearFlags.SolidColor;
        _clueCamera.backgroundColor = Color.clear;

        // 정지된 단서 이미지라면 한 번만 촬영하면 됨
        _clueCamera.Render();
    }

    private void OnDestroy()
    {
        if (_createdModule != null)
        {
            Destroy(_createdModule);
        }
    }
}
