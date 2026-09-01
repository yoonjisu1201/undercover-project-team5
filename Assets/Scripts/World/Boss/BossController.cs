using System;
using System.Collections.Generic;
using Unity.Behavior;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// 보스의 Behavior 그래프가 돌기 위한 주변 정리를 맡는다. 판단은 전부 그래프가 한다.
//
// 하는 일:
//  - 서버가 아닌 인스턴스에서 NavMeshAgent가 트랜스폼을 건드리지 않게 막는다(NetworkTransform과 충돌).
//  - 먼 모듈로 순간이동시키고, 위치를 알 수 없는 쿵 소리로 "옮겨왔다"는 것만 알린다.
//  - 발소리를 낸다. 소리는 전부 SoundManager/SoundData 를 거친다.
//    옮긴 뒤에는 그래프를 다시 시작해서 이동 노드가 목적지를 새로 잡게 한다.
//  - 이동 속도를 애니메이터 파라미터로 옮긴다. Behavior의 이동 노드는 float 하나만 쓰는데
//    이 프로젝트 애니메이터는 걷기/달리기 bool 두 개를 쓰기 때문이다. 라운드마다 외형이 바뀌고
//    외형마다 자기 Animator를 들고 있어서 NetworkAnimator로 동기화할 수 없다. 대신 각 클라이언트가
//    동기화된 위치 변화량으로 속도를 계산해 자기 화면의 Animator에 직접 넣는다.
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(BehaviorGraphAgent))]
[RequireComponent(typeof(BossVisual))]
public class BossController : NetworkBehaviour
{
    [Header("순간이동")]
    [Tooltip("모듈 바닥에서 이만큼 띄운 곳을 도착 후보로 삼는다. 바닥에 딱 붙이면 NavMesh 샘플링이 실패할 수 있다.")]
    [SerializeField, Min(0f)] private float _moduleFloorOffset = 0.5f;

    [Tooltip("이 거리 안에 사람이 있는 모듈로는 옮기지 않는다. 눈앞에 나타나면 대응할 여지가 없다.")]
    [SerializeField, Min(0f)] private float _teleportMinPlayerDistance = 25f;

    [Tooltip("사라진 뒤 다시 나타나기까지의 시간(초). 사라지는 소리(Teleport 3_1) 길이에 맞춰 둔 값이다. "
        + "이 시간 동안 보스는 보이지도 않고 쫓지도 않으므로, 소리보다 짧게 잡아도 된다.")]
    [SerializeField, Min(0f)] private float _teleportHiddenSeconds = 3f;

    [Header("애니메이션")]
    [SerializeField] private string _walkParameter = "IsWalking";
    [SerializeField] private string _runParameter = "IsRunning";

    [Tooltip("이 속도 이상이면 달리는 것으로 본다. 어느 속도와도 같은 값을 쓰면 안 된다 - "
        + "실제 속도가 그 값 근처에서 흔들려 걷기/달리기 모션이 매 프레임 뒤바뀐다. "
        + "4.0 은 기척 추격(3.5)과 추격(4.6) 사이라, 눈으로 보고 쫓을 때만 달린다.")]
    [SerializeField, Min(0f)] private float _runSpeedThreshold = 4f;

    [Tooltip("걸어 다닐 때 좌우로 살피는 각도(±도). 0이면 두리번거리지 않는다.")]
    [SerializeField, Min(0f)] private float _lookAroundAngle = 35f;

    [Tooltip("한 번 살피고 다음까지의 평균 간격(초). 실제로는 여기에 ±30%를 섞는다. "
        + "가끔 하는 동작이라 짧게 잡으면 안 된다 - 계속 두리번거리면 산만해 보인다.")]
    [SerializeField, Min(0.1f)] private float _lookAroundInterval = 20f;

    [Tooltip("한 번 살피는 데 걸리는 시간(초). 왼쪽 - 오른쪽 - 정면으로 한 바퀴다.")]
    [SerializeField, Min(0.1f)] private float _lookAroundDuration = 2.5f;

    [SerializeField, Min(0f)] private float _walkSpeedThreshold = 0.2f;

    [Header("발소리")]
    [Tooltip("걸을 때 발소리 간격(초). 보스는 사람보다 느리고 무겁게 걷는다.")]
    [SerializeField, Min(0.05f)] private float _footstepWalkInterval = 0.7f;

    [SerializeField, Min(0.05f)] private float _footstepRunInterval = 0.45f;

    private NavMeshAgent _agent;
    private BehaviorGraphAgent _brain;
    private BossVisual _visual;
    private Vector3 _lastAnimationPosition;
    private readonly FootstepLoop _footsteps = new(SoundKey.Boss_FootstepWalk, SoundKey.Boss_FootstepRun);
    private UndergroundRandomMapGenerator _mapGenerator;

    // 순간이동 직후 그래프를 다시 시작해야 하는지. 노드 실행 중에 Restart를 부르면 재진입이
    // 되므로 한 프레임 미뤄서 처리한다.
    private bool _restartGraphPending;

    // 굳음 판정용. 마지막으로 의미 있게 움직인 지점과 그 뒤로 흐른 시간.
    private Vector3 _stuckAnchor;
    private float _stuckSeconds;
    private BossDormancy _dormancy;
    private BossAttack _attack;
    private BossTargetMemory _memory;

    // 이 시간 넘게 제자리에 있으면 굳은 것으로 본다.
    // 그래프의 Wait 노드 중 가장 긴 것(0.5초)과 공격 쿨다운(2초)보다 길게 둬야 정상 대기를 끊지 않는다.
    private const float StuckRecoverySeconds = 3f;

    // 이 거리 안에서만 움직였으면 제자리로 본다.
    private const float StuckMoveThreshold = 0.3f;

    // 순간이동으로 모습을 감추고 있는 중인지와, 다시 나타나기까지 남은 시간.
    private bool _isHidden;
    private float _hiddenRemainingSeconds;

    // 발소리를 낸 지점과 그 소리의 키. 소리가 사람에게 닿았는지는 듣는 쪽이 판단한다.
    // 보스는 라운드마다 새로 스폰돼 미리 참조를 잡아둘 수 없으므로 정적 이벤트로 알린다.
    public static event Action<Vector3, SoundKey> FootstepPlayed;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _brain = GetComponent<BehaviorGraphAgent>();
        _visual = GetComponent<BossVisual>();
        _dormancy = GetComponent<BossDormancy>();
        _attack = GetComponent<BossAttack>();
        _memory = GetComponent<BossTargetMemory>();
        _lastAnimationPosition = transform.position;
        _stuckAnchor = transform.position;
    }

    public override void OnNetworkSpawn()
    {
        // 서버가 아닌 인스턴스는 NavMeshAgent가 스스로 트랜스폼을 갱신하지 않게 해서
        // NetworkTransform이 동기화한 값과 충돌하지 않게 한다.
        if (!IsServer)
        {
            _agent.updatePosition = false;
            _agent.updateRotation = false;

            // 판단은 서버만 한다. BehaviorGraphAgent 의 NetcodeRunOnlyOnOwner 는 소유자 기준이라
            // 소유권이 클라이언트로 재분배되면 그쪽에서 그래프가 돌아버린다. 위치는 서버 권한이므로
            // 그 순간 보스가 얼어붙는다. 그래서 서버 여부로 직접 끈다.
            _brain.enabled = false;
            return;
        }

        // 순간이동 목적지를 고를 때 모듈 목록이 필요하다.
        _mapGenerator = FindFirstObjectByType<UndergroundRandomMapGenerator>();
    }

    public override void OnNetworkDespawn()
    {
        _mapGenerator = null;
    }

    private void Update()
    {
        // 애니메이션은 각 클라이언트가 자기 화면의 Animator에 직접 넣는다.
        UpdateAnimatorState();

        // 사라져 있는 시간은 각 클라이언트가 따로 센다. 위치가 이미 동기화돼 있어 통신이 필요 없다.
        UpdateTeleportHide();

        if (!IsServer)
        {
            return;
        }

        if (_restartGraphPending)
        {
            _restartGraphPending = false;
            _brain.Restart();
            _stuckSeconds = 0f;
            return;
        }

        // 사라져 있는 동안 제자리에 서 있는 것은 굳은 것이 아니라 의도된 상황이다.
        if (_isHidden)
        {
            return;
        }

        UpdateStuckWatchdog();
    }

    // 걸어 다니는 동안 가끔 좌우를 살핀다. 가는 방향을 기준으로 얹는 각도라 경로는 그대로다.
    //
    // 에이전트가 회전을 잡은 뒤에 얹어야 해서 LateUpdate 다. 얹은 각도는 다음 프레임에 먼저
    // 걷어낸다. 그냥 두면 에이전트가 틀어진 각도에서 다시 돌기 시작해 편향이 쌓인다.
    private void LateUpdate()
    {
        if (!IsServer)
        {
            return;
        }

        transform.rotation *= Quaternion.Inverse(_lookOffset);
        _lookOffset = Quaternion.identity;

        if (!ShouldLookAround())
        {
            _lookStartTime = float.NegativeInfinity;
            return;
        }

        float now = Time.time;
        float elapsed = now - _lookStartTime;

        if (elapsed > _lookAroundDuration)
        {
            if (now < _lookNextTime)
            {
                return;
            }

            _lookStartTime = now;
            _lookNextTime = now + _lookAroundInterval * UnityEngine.Random.Range(0.7f, 1.3f);
            elapsed = 0f;
        }

        // 사인 한 바퀴면 왼쪽 - 정면 - 오른쪽 - 정면 순서가 그대로 나온다.
        float yaw = Mathf.Sin(elapsed / _lookAroundDuration * Mathf.PI * 2f) * _lookAroundAngle;

        _lookOffset = Quaternion.Euler(0f, yaw, 0f);
        transform.rotation *= _lookOffset;
    }

    // 걸어 다닐 때만 살핀다. 눈으로 보고 쫓는 중(달리기 속도)에는 시야가 같이 흔들려서
    // 쫓던 사람을 놓친다. 공격 중이나 잠복 중에도 하지 않는다.
    private bool ShouldLookAround()
    {
        if (_isHidden || _lookAroundAngle <= 0f || _agent == null || !_agent.isOnNavMesh)
        {
            return false;
        }

        if ((_dormancy != null && _dormancy.IsDormant) ||
            (_attack != null && (_attack.IsAttacking || _attack.IsOnCooldown)))
        {
            return false;
        }

        float speed = _agent.velocity.magnitude;
        return speed > _walkSpeedThreshold && speed < _runSpeedThreshold;
    }

    // 보스가 이유 없이 굳어 있으면 그래프를 다시 시작한다.
    //
    // 경로 상태로 판정하면 안 된다. 굳는 원인이 여러 가지인데(경로가 무효해진 이동 노드,
    // 꺼지지 않은 isStopped, 도달 판정이 안 서는 목적지) 그중 일부는 hasPath 가 켜진 채로
    // 멈춰 있어서 경로 기준 판정에는 아예 걸리지 않는다. 그래서 "실제로 안 움직였는지"만 본다.
    //
    // 패키지의 Navigate 노드는 경로가 PathInvalid 가 되면 실패로 빠져나오지 못하고 영원히
    // Running 을 돌려준다(PathPartial 만 걸러낸다). 그 노드를 감싼 Repeat While 은 자식이
    // 끝난 뒤에만 조건을 다시 보고, Selector 직속 자식이 아니라서 Observer Abort 도 걸 수 없다.
    // 그래서 한 번 매달리면 스스로 나올 길이 없다 — 사람이 바로 뒤에 있어도, 소리가 나도
    // 그 노드에 갇힌 채로 서 있게 된다. 패키지 노드를 고칠 수 없으니 밖에서 끊는다.
    private Quaternion _lookOffset = Quaternion.identity;
    private float _lookStartTime = float.NegativeInfinity;
    private float _lookNextTime;

    private void UpdateStuckWatchdog()
    {
        // 멈춰 있는 것이 의도된 상황은 제외한다. 잠복, 공격 모션, 공격 사이의 대기,
        // 수색을 포기한 뒤의 휴식이 그렇다. 휴식은 이 감시 시간보다 길게 잡혀 있어서
        // 빼놓지 않으면 쉬는 동안 행동 트리가 계속 다시 시작된다.
        bool shouldBeMoving = (_dormancy == null || !_dormancy.IsDormant)
            && (_attack == null || (!_attack.IsAttacking && !_attack.IsOnCooldown))
            && (_memory == null || !_memory.IsResting);

        Vector3 position = transform.position;
        if (!shouldBeMoving ||
            (position - _stuckAnchor).sqrMagnitude > StuckMoveThreshold * StuckMoveThreshold)
        {
            _stuckAnchor = position;
            _stuckSeconds = 0f;
            return;
        }

        _stuckSeconds += Time.deltaTime;
        if (_stuckSeconds < StuckRecoverySeconds)
        {
            return;
        }

        _stuckAnchor = position;
        _stuckSeconds = 0f;
        Debug.LogWarning(
            $"[보스] {StuckRecoverySeconds}초 동안 제자리에 있어 행동 트리를 다시 시작합니다. " +
            $"위치={position}, 경로={_agent.pathStatus}, hasPath={_agent.hasPath}, isStopped={_agent.isStopped}",
            this);
        _brain.Restart();
    }

    // 서버는 NavMeshAgent 속도를 그대로 쓰고, 클라이언트는 동기화된 위치 변화량으로 속도를 낸다.
    // 두 값이 정확히 같지는 않지만 걷기/달리기를 가르는 데는 충분하다.
    private void UpdateAnimatorState()
    {
        // 위치는 Animator 가 없는 프레임에도 갱신해야 한다. 안 그러면 외형 모델이 붙는 순간
        // 몇 프레임 전 위치와 비교되어 속도가 크게 튀고, 멈춰 있어도 달리기 모션이 한 번 나온다.
        Vector3 position = transform.position;
        float clientSpeed = Vector3.Distance(position, _lastAnimationPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        _lastAnimationPosition = position;

        Animator animator = _visual != null ? _visual.ActiveAnimator : null;
        if (animator == null)
        {
            return;
        }

        float speed = IsServer ? _agent.velocity.magnitude : clientSpeed;

        bool walking = speed > _walkSpeedThreshold;
        bool running = speed >= _runSpeedThreshold;
        animator.SetBool(_walkParameter, walking);
        animator.SetBool(_runParameter, running);

        UpdateFootstep(walking, running);
    }

    // 발소리도 각 클라이언트가 자기 화면에서 낸다. 위치가 이미 동기화돼 있어 통신이 필요 없다.
    private void UpdateFootstep(bool walking, bool running)
    {
        // 사라져 있는 동안 발소리가 나면 옮겨간 자리가 그대로 드러난다. Warp 직후에는 위치가 크게
        // 튀어서 클라이언트 쪽 속도 계산이 달리는 것으로 잡기도 하는데, 그 한 걸음까지 여기서 막힌다.
        if (!walking || _isHidden)
        {
            _footsteps.Stop();
            return;
        }

        if (_footsteps.Tick(running, _footstepWalkInterval, _footstepRunInterval))
        {
            Vector3 position = transform.position;
            _footsteps.Play(position, running);
            FootstepPlayed?.Invoke(position, _footsteps.KeyFor(running));
        }
    }

    #region 순간이동

    // 사람들에게서 가장 먼 모듈로 옮긴다. 성공하면 위치를 알 수 없는 쿵 소리를 모두에게 들려준다.
    // 그래프의 순간이동 노드가 호출한다.
    public bool TeleportToDistantModule()
    {
        if (!IsServer || _mapGenerator == null)
        {
            return false;
        }

        List<Vector3> playerPositions = CollectPlayerPositions();

        Vector3 bestPosition = Vector3.zero;
        float bestDistance = _teleportMinPlayerDistance;
        bool found = false;

        foreach (UndergroundModule module in _mapGenerator.PlacedModules)
        {
            if (module == null || module.Bounds == null)
            {
                continue;
            }

            Vector3 candidate = module.Bounds.bounds.center;
            candidate.y = module.Bounds.bounds.min.y + _moduleFloorOffset;

            // 가장 가까운 사람과의 거리로 평가한다. 한 명에게서 멀어도 다른 사람 옆이면 의미가 없다.
            float nearest = NearestDistance(candidate, playerPositions);
            if (nearest < bestDistance)
            {
                continue;
            }

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                continue;
            }

            bestDistance = nearest;
            bestPosition = hit.position;
            found = true;
        }

        if (!found)
        {
            return false;
        }

        // 소리는 "보스가 있던 자리"에서 나야 한다. Warp 뒤에 읽으면 도착 지점이 되어,
        // 어디로 갔는지 알려주는 소리가 되어 버린다.
        Vector3 departurePosition = transform.position;

        // Warp 는 경로를 버리고 위치만 옮긴다. 이동 중이던 경로가 남으면 새 자리에서 되돌아가려 한다.
        //
        // 소리가 끝나기를 기다렸다 옮기지 않는다. 그러면 사라져 있는 동안 보스가 보이지 않는 채로
        // 떠난 자리에 서 있게 되어, 옆에 있던 사람이 보이지 않는 것에게 맞는다.
        _agent.Warp(bestPosition);

        if (_agent.isOnNavMesh)
        {
            _agent.ResetPath();
            _agent.isStopped = true;
        }

        // 사라져 있는 동안은 판단도 멈춘다. 보이지 않는 채로 돌아다니면 도착한 자리가 드러난다.
        _brain.enabled = false;

        StartTeleportHideRpc(departurePosition);
        return true;
    }

    private List<Vector3> CollectPlayerPositions()
    {
        var positions = new List<Vector3>();

        foreach (PlayerHealth survivor in SurvivorRegistry.Active())
        {
            positions.Add(survivor.transform.position);
        }

        return positions;
    }

    // 사람이 아무도 없으면 어디로 가도 되므로 무한대를 돌려준다.
    private static float NearestDistance(Vector3 point, List<Vector3> positions)
    {
        float nearest = float.MaxValue;
        foreach (Vector3 position in positions)
        {
            nearest = Mathf.Min(nearest, Vector3.Distance(point, position));
        }

        return nearest;
    }

    // 떠난 자리에서 사라지는 소리를 내고 모습을 감춘다. 근처에 있던 사람은 "옆에 있던 것이
    // 사라졌다"를 알아채고, 멀리 있던 사람에게는 들리지 않는다.
    //
    // 위치는 인자로 받는다. 이 시점이면 NetworkTransform 이 이미 새 위치를 퍼뜨렸을 수 있어서
    // 클라이언트에서 transform.position 을 읽으면 도착 지점이 나온다.
    [Rpc(SendTo.Everyone)]
    private void StartTeleportHideRpc(Vector3 departurePosition)
    {
        SoundManager.Instance?.PlayAt(SoundKey.Boss_Disappear, departurePosition);
        _footsteps.Stop();
        _visual.SetVisible(false);

        _isHidden = true;
        _hiddenRemainingSeconds = _teleportHiddenSeconds;
    }

    // 사라지는 소리가 끝나면 도착한 자리에서 나타나는 소리와 함께 다시 모습을 드러낸다.
    private void UpdateTeleportHide()
    {
        if (!_isHidden)
        {
            return;
        }

        _hiddenRemainingSeconds -= Time.deltaTime;
        if (_hiddenRemainingSeconds > 0f)
        {
            return;
        }

        _isHidden = false;
        _visual.SetVisible(true);
        SoundManager.Instance?.PlayAt(SoundKey.Boss_Appear, transform.position);

        if (IsServer)
        {
            ResumeAfterTeleport();
        }
    }

    private void ResumeAfterTeleport()
    {
        _brain.enabled = true;

        if (_agent.isOnNavMesh)
        {
            _agent.isStopped = false;
        }

        // 이동 노드는 "표적 위치가 바뀔 때만" 목적지를 다시 잡는다. Warp 로 경로가 사라져도
        // 표적은 그대로이므로 목적지를 다시 잡지 않고, 결과적으로 새 자리에서 멈춰 선다.
        // 그래프를 다시 시작해서 이동 노드가 목적지를 새로 계산하게 만든다.
        _restartGraphPending = true;

        // 옮겨오기 전의 목적지를 버린다. 그대로 두면 새 자리에 나타나자마자 지도 반대편의
        // 흔적으로 되돌아 걸어가서, 도망치려고 옮긴 것이 아무 의미가 없어진다.
        if (_memory != null)
        {
            _memory.ForgetDestination();
        }

        // 사라져 있던 시간은 굳은 것이 아니다. 감시 타이머를 새 자리 기준으로 되돌린다.
        _stuckAnchor = transform.position;
        _stuckSeconds = 0f;

    }

    #endregion

}
