using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
// 미니게임 완료 단서를 완료한 플레이어 방향으로 튕겨 내보낸다.
public sealed class MiniGameRewardLauncher : MonoBehaviour
{
    private const float ColliderDisableDuration = 0.25f;

    [SerializeField, Min(0f)] private float _launchSpeed = 2f;
    [SerializeField, Min(0f)] private float _upwardSpeed = 0.8f;
    [SerializeField, Min(0f)] private float _spawnLift = 0.15f;

    // 서버에서 생성된 단서에 플레이어 방향의 초기 속도를 적용한다.
    public void Launch(GameObject rewardObject, Vector3 spawnPosition, Vector3 targetPlayerPosition)
    {
        if (rewardObject == null ||
            !rewardObject.TryGetComponent(out Rigidbody rigidbody))
        {
            return;
        }

        // 기계나 바닥 콜라이더에 끼이지 않도록 먼저 위로 올린 뒤 수평 방향과 상승 속도를 나눠 적용한다.
        Vector3 launchPosition = spawnPosition + Vector3.up * _spawnLift;
        rigidbody.position = launchPosition;
        rigidbody.isKinematic = false;
        StartCoroutine(TemporarilyDisableColliders(rewardObject));

        Vector3 launchDirection = targetPlayerPosition - launchPosition;
        launchDirection.y = 0f;
        float remainingDistance = launchDirection.magnitude;
        launchDirection = launchDirection.sqrMagnitude > 0.001f
            ? launchDirection.normalized
            : transform.forward;

        // 플레이어가 가까우면 발밑까지 지나치지 않도록 전진 속도를 거리에 맞춰 줄인다.
        float adjustedLaunchSpeed = Mathf.Min(
            _launchSpeed,
            Mathf.Max(0.25f, remainingDistance * 0.75f));
        rigidbody.linearVelocity =
            launchDirection * adjustedLaunchSpeed +
            Vector3.up * _upwardSpeed;
    }

    // 생성 직후 기계 콜라이더에 부딪혀 뒤로 밀리지 않도록 단서 충돌을 잠시 막는다.
    private static IEnumerator TemporarilyDisableColliders(GameObject rewardObject)
    {
        Collider[] colliders = rewardObject.GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders)
        {
            collider.enabled = false;
        }

        yield return new WaitForSeconds(ColliderDisableDuration);

        foreach (Collider collider in colliders)
        {
            if (collider != null)
            {
                collider.enabled = true;
            }
        }
    }
}
