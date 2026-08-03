using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 4. 파츠 Clone 생성부터 카메라 촬영과 Texture2D 복사까지 담당한다.
public sealed class ClueModuleCapture : IDisposable
{
    private const float FramingMargin = 1.25f;
    private const float MinZoom = 2f;
    private const float MaxZoom = 3f;

    private readonly MonoBehaviour _owner;
    private readonly Transform _moduleSpawnPoint;
    private readonly Camera _clueCamera;
    private readonly RenderTexture _renderTexture;
    private readonly float _baseFieldOfView;
    private readonly Vector3 _frontViewDirection;

    private GameObject _createdModule;
    private Mesh _createdMesh;

    // 생성된 파츠 Clone을 카메라 촬영 후 제거한다.
    public ClueModuleCapture(MonoBehaviour owner, Transform moduleSpawnPoint, Camera clueCamera, RenderTexture renderTexture)
    {
        _owner = owner;
        _moduleSpawnPoint = moduleSpawnPoint;
        _clueCamera = clueCamera;
        _renderTexture = renderTexture;
        _baseFieldOfView = clueCamera.fieldOfView;
        _frontViewDirection = (moduleSpawnPoint.position - clueCamera.transform.position).normalized;

        InitializeCamera();
    }

    // Clone된 파츠를 카메라로 촬영하고 Texture2D로 복사한다.
    public async UniTask<Texture2D> CaptureAsync(GameObject sourceModule, CancellationToken cancellationToken)
    {
        _createdModule = CreateSinglePartPreview(sourceModule);
        if (_createdModule == null || !_createdModule.TryGetComponent(out Renderer moduleRenderer))
        {
            Debug.LogError($"[ClueModuleCapture] {sourceModule.name}에서 Renderer를 찾지 못했습니다.");
            return null;
        }

        CenterModule(moduleRenderer);
        PositionCamera(moduleRenderer.bounds);

        // 한 프레임만 렌더링한 뒤 결과를 독립적인 Texture2D로 저장한다.
        _clueCamera.enabled = true;
        try
        {
            await UniTask.WaitForEndOfFrame(_owner, cancellationToken);
            return CopyRenderTexture();
        }
        finally
        {
            _clueCamera.enabled = false;
        }
    }

    private void InitializeCamera()
    {
        _clueCamera.enabled = false;
        _clueCamera.targetTexture = _renderTexture;
        _clueCamera.clearFlags = CameraClearFlags.SolidColor;
        _clueCamera.backgroundColor = Color.clear;
    }

    private GameObject CreateSinglePartPreview(GameObject sourceModule)
    {
        GameObject previewModule = new($"{sourceModule.name}_Preview");
        previewModule.transform.SetParent(_moduleSpawnPoint, false);

        // 스킨 파츠는 현재 포즈가 적용된 Mesh로 Bake한다.
        // 일부 에셋은 렌더러가 파츠 루트가 아니라 자식 오브젝트에 붙어있으므로 자식까지 탐색한다.
        SkinnedMeshRenderer skinnedRenderer = sourceModule.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skinnedRenderer != null)
        {
            _createdMesh = new Mesh();
            skinnedRenderer.BakeMesh(_createdMesh, true);
            previewModule.AddComponent<MeshFilter>().sharedMesh = _createdMesh;
            previewModule.AddComponent<MeshRenderer>().sharedMaterials = skinnedRenderer.sharedMaterials;
            return previewModule;
        }

        // 일반 파츠는 원본 Mesh와 Material을 그대로 참조한다.
        MeshFilter sourceMeshFilter = sourceModule.GetComponentInChildren<MeshFilter>(true);
        MeshRenderer sourceMeshRenderer = sourceModule.GetComponentInChildren<MeshRenderer>(true);
        if (sourceMeshFilter != null && sourceMeshRenderer != null)
        {
            previewModule.AddComponent<MeshFilter>().sharedMesh = sourceMeshFilter.sharedMesh;
            previewModule.AddComponent<MeshRenderer>().sharedMaterials = sourceMeshRenderer.sharedMaterials;
            return previewModule;
        }

        UnityEngine.Object.Destroy(previewModule);
        return null;
    }

    private void CenterModule(Renderer moduleRenderer)
    {
        _createdModule.transform.position += _moduleSpawnPoint.position - moduleRenderer.bounds.center;
    }

    // 카메라를 파츠의 단서 UI 프레임에 맞게 위치시키고 스케일을 조정한다.
    private void PositionCamera(Bounds bounds)
    {
        Vector3 viewDirection = _frontViewDirection;
        if (viewDirection == Vector3.zero)
        {
            viewDirection = Vector3.forward;
        }

        float halfFov = _baseFieldOfView * 0.5f * Mathf.Deg2Rad;
        float verticalDistance = bounds.extents.y / Mathf.Tan(halfFov);
        float horizontalDistance = bounds.extents.x / (Mathf.Tan(halfFov) * _clueCamera.aspect);
        float distance = (Mathf.Max(verticalDistance, horizontalDistance) + bounds.extents.z) * FramingMargin;
        float zoom = (MinZoom + MaxZoom) * 0.5f;

        // 기존 카메라가 바라보던 반대편에 고정해 모듈의 앞면을 촬영한다.
        // 매 촬영마다 현재 카메라 위치로 방향을 다시 계산하면 앞/뒤가 번갈아 바뀔 수 있다.
        _clueCamera.transform.position = bounds.center + viewDirection * Mathf.Max(distance, 0.1f);
        _clueCamera.transform.LookAt(bounds.center);
        _clueCamera.fieldOfView = _baseFieldOfView / zoom;
    }

    // RenderTexture를 Texture2D로 복사한다.
    private Texture2D CopyRenderTexture()
    {
        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = _renderTexture;
            Texture2D texture = new(
                _renderTexture.width,
                _renderTexture.height,
                TextureFormat.RGBA32,
                false);

            texture.ReadPixels(new Rect(0, 0, _renderTexture.width, _renderTexture.height), 0, 0);
            texture.Apply();
            return texture;
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    public void ReleasePreview()
    {
        if (_createdModule != null)
        {
            UnityEngine.Object.Destroy(_createdModule);
            _createdModule = null;
        }

        if (_createdMesh != null)
        {
            UnityEngine.Object.Destroy(_createdMesh);
            _createdMesh = null;
        }
    }

    public void Dispose()
    {
        ReleasePreview();
    }
}
