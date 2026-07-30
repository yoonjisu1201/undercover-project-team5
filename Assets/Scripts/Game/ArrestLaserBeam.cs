using System;
using UnityEngine;

// 원본 레이저 효과만으로는 카메라 조준 방향과 게임의 충돌 규칙을 정확히 반영하기 어렵다.
// 검거 도구의 레이저가 PlayerAimIK와 같은 방향을 바라보고, 실제로 맞은 지점까지만 보이도록 제어하는 스크립트다.
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public sealed class ArrestLaserBeam : MonoBehaviour
{
    [SerializeField] private Transform hitEffect;
    [SerializeField] private float maxLength = 15f;
    [SerializeField] private float beamWidth = 0.02f;
    [SerializeField] private float mainTextureLength = 0.25f;
    [SerializeField] private float noiseTextureLength = 0.3f;
    [SerializeField] private float textureScaleReference = 0.08f;

    private LineRenderer lineRenderer;
    private PlayerAimIK aimIK;
    private Material beamMaterial;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        // 공유 재질을 바꾸지 않고 이 레이저의 텍스처 반복만 조절할 수 있도록 재질 인스턴스를 사용한다.
        beamMaterial = lineRenderer.material;
        aimIK = transform.root.GetComponent<PlayerAimIK>();

        if (hitEffect == null)
        {
            hitEffect = transform.Find("Hit");
        }
    }

    private void OnEnable()
    {
        lineRenderer ??= GetComponent<LineRenderer>();
        lineRenderer.enabled = true;
        lineRenderer.widthMultiplier = beamWidth;
    }

    private void LateUpdate()
    {
        Vector3 origin = transform.position;
        Transform ownerRoot = transform.root;

        // IK가 있으면 카메라의 상하 조준을 반영하고, 없으면 플레이어의 정면을 사용한다.
        Vector3 direction = aimIK != null
            ? aimIK.AimDirection
            : Vector3.ProjectOnPlane(ownerRoot.forward, ownerRoot.up).normalized;
        Vector3 endPosition = origin + direction * maxLength;

        // 총구에서 플레이어 자신을 제외한 가장 가까운 충돌 지점을 찾는다.
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            maxLength,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        bool hasHit = false;
        foreach (RaycastHit hit in hits)
        {
            // 플레이어 몸과 장비의 Collider는 레이저를 막지 않는다.
            if (hit.transform.IsChildOf(ownerRoot))
            {
                continue;
            }

            // NPC의 BoxCollider만 타격 판정에 사용하고, 다른 Trigger는 통과한다.
            if (hit.collider.isTrigger)
            {
                bool isNpcHitBox = hit.collider is BoxCollider
                    && hit.collider.GetComponentInParent<NpcMovement>() != null;
                if (!isNpcHitBox)
                {
                    continue;
                }
            }

            endPosition = hit.point;
            hasHit = true;
            break;
        }

        lineRenderer.SetPosition(0, origin);
        lineRenderer.SetPosition(1, endPosition);
        UpdateMaterialTiling(Vector3.Distance(origin, endPosition));

        // 충돌했을 때만 끝점 효과를 켜고 실제 충돌 위치로 옮긴다.
        if (hitEffect != null)
        {
            hitEffect.gameObject.SetActive(hasHit);
            if (hasHit)
            {
                hitEffect.position = endPosition;
            }
        }
    }

    private void UpdateMaterialTiling(float distance)
    {
        if (beamMaterial == null)
        {
            return;
        }

        // 레이저 길이가 달라져도 무늬가 늘어나지 않도록 거리에 맞춰 텍스처 반복 횟수를 보정한다.
        float referenceScale = Mathf.Max(0.0001f, textureScaleReference);
        if (beamMaterial.HasProperty("_MainTex"))
        {
            beamMaterial.SetTextureScale(
                "_MainTex",
                new Vector2(mainTextureLength * distance / referenceScale, 1f));
        }

        if (beamMaterial.HasProperty("_Noise"))
        {
            beamMaterial.SetTextureScale(
                "_Noise",
                new Vector2(noiseTextureLength * distance / referenceScale, 1f));
        }
    }
}
