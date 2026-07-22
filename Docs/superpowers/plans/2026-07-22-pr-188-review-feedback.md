# PR #188 Review Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PR #188의 미해결 리뷰 8개를 반영해 방해 이벤트의 서버 권한, 경고 취소, 상태 중복 방지, 종료 사유 전달, 중복 매니저 제거 및 책임 분리를 완료한다.

**Architecture:** `InterferenceEventManager`는 서버 권한·네트워크 시각·RPC·라운드 구독만 담당한다. 이벤트 검색은 `InterferenceEventRegistry`, 로컬 경고/활성/종료 전이는 `InterferenceEventLocalController`, 실제 시야 효과 상태와 UnityEvent 호출은 `FieldVisionInterferenceEvent`가 담당한다. 이벤트 ID와 종료 사유 enum은 별도 enum 파일을 만들지 않고 계약 파일인 `IInterferenceEvent.cs`에 함께 둔다.

**Tech Stack:** Unity 6000.3.15f1, C#, Netcode for GameObjects 2.13.0, Input System 1.19.0, Unity Test Framework 1.6.0

## Global Constraints

- 기존 정책인 “서버에서 한 번에 하나의 방해 이벤트만 실행”은 유지한다.
- `Replaced`는 서버의 실행 중 이벤트 교체 기능을 새로 추가하지 않고, 새 시작 RPC 수신 시 남아 있는 로컬 효과를 정리하는 사유로만 사용한다.
- `InterferenceEventId.cs`를 삭제할 때 `InterferenceEventId.cs.meta`도 함께 삭제한다.
- 기존 씬의 `_onInterferenceEnded` 직렬화 필드는 유지하고, 종료 사유용 UnityEvent를 별도 추가해 씬 참조 손실을 피한다.
- 관련 없는 `PlayScene.unity` YAML, 주석, 포맷은 수정하지 않는다.
- 현재 프로젝트에는 테스트 asmdef가 없으므로 이번 리뷰 대응을 위해 전체 런타임 어셈블리를 재구성하지 않는다. Unity 컴파일과 Multiplayer Play Mode 시나리오로 검증한다.
- GitHub 댓글 답변 및 스레드 해결은 별도 명시적 요청 전에는 수행하지 않는다.

---

## Feedback-to-Code Validation

| # | 리뷰 피드백 | 실제 코드 확인 | 판정 | 반영 위치 |
|---|---|---|---|---|
| 1 | `UnityEngine.LightTransport` 삭제 | `InterferenceEventManager.cs:5`에 있으나 사용하지 않음 | 즉시 반영 | Task 1 |
| 2 | `UnityEditor.PackageManager` 삭제 | `InterferenceEventManager.cs:3`에 있으나 사용하지 않음. 런타임 스크립트의 Editor 의존성은 플레이어 빌드 위험 | 즉시 반영 | Task 1 |
| 3 | `Update`의 `!IsSpawned` 로그 반복 | 스폰 전에도 `Update`가 호출되므로 매 프레임 `LogError` 가능 | 로그 없이 조기 반환 | Task 1 |
| 4 | `StopCurrentEvent`에 서버 검사 필수 | public 메서드가 `_serverEventId` 변경 후 RPC 호출. 현재 내부 호출은 서버 경로지만 외부 호출을 막지 못함 | 서버 가드 추가 | Task 1 |
| 5 | 중복 인스턴스를 Destroy하지 않음 | `Awake`에서 `enabled=false`만 수행해 중복 GameObject/NetworkObject가 남음. 다른 매니저들은 `Destroy(gameObject)` 패턴 사용 | Destroy로 통일 | Task 1 |
| 6 | enum을 별도 파일로 두지 말고 종료 사유 추가 | 현재 ID enum이 단독 파일이며 종료 사유가 없음 | ID/종료 사유를 계약 파일로 이동 | Task 2 |
| 7 | 인터페이스에 경고 취소, 상태 조회, 종료 사유 필요 | 현재 `ShowWarning/Activate/Deactivate`만 있고 구현체 상태가 없음 | 계약 및 구현 확장 | Task 2 |
| 8 | Manager가 400줄이며 책임이 과다 | 네트워크, registry, 로컬 수명 주기, 효과 조회를 모두 처리 | registry와 local controller 추출 | Task 3 |

## File Structure

- Modify: `Assets/Scripts/Event/IInterferenceEvent.cs` — ID/종료 사유 enum과 이벤트 수명 주기 계약
- Delete: `Assets/Scripts/Event/InterferenceEventId.cs`
- Delete: `Assets/Scripts/Event/InterferenceEventId.cs.meta`
- Modify: `Assets/Scripts/Event/FieldVisionInterferenceEvent.cs` — 경고/활성 상태와 종료 사유를 실제 UnityEvent로 전달
- Create: `Assets/Scripts/Event/InterferenceEventRegistry.cs` — ID와 구현체 간 등록/검색만 담당
- Create: `Assets/Scripts/Event/InterferenceEventRegistry.cs.meta` — Unity 생성 후 Git 추적
- Create: `Assets/Scripts/Event/InterferenceEventLocalController.cs` — 로컬 경고/활성/종료 시각과 전이만 담당
- Create: `Assets/Scripts/Event/InterferenceEventLocalController.cs.meta` — Unity 생성 후 Git 추적
- Modify: `Assets/Scripts/Event/InterferenceEventManager.cs` — 서버 권한, RPC, 라운드 구독 및 두 협력 객체 조정

---

### Task 1: 런타임 안전성과 서버 권한 보정

**Files:**
- Modify: `Assets/Scripts/Event/InterferenceEventManager.cs:1`

**Interfaces:**
- Consumes: 기존 `NetworkBehaviour.IsSpawned`, `NetworkBehaviour.IsServer`
- Produces: 서버가 아닌 호출은 상태/RPC를 변경하지 않는 `StopCurrentEvent()`

- [ ] **Step 1: 현재 문제 위치를 기준선으로 확인**

Run:

```powershell
rg -n "UnityEditor.PackageManager|UnityEngine.LightTransport|LogError.*IsSpawned|enabled = false|public void StopCurrentEvent" Assets/Scripts/Event/InterferenceEventManager.cs
```

Expected: 현재 import 2개, 반복 오류 로그, `enabled = false`, 서버 가드 없는 `StopCurrentEvent`가 모두 검색된다.

- [ ] **Step 2: import, 스폰 가드, 중복 객체 처리를 수정**

적용할 핵심 코드는 다음과 같다.

```csharp
using Unity.Netcode;
using UnityEngine;
using Debug = UnityEngine.Debug;

private void Awake()
{
    if (Instance != null && Instance != this)
    {
        Destroy(gameObject);
        return;
    }

    Instance = this;
}

private void Update()
{
    if (!IsSpawned)
    {
        return;
    }

    double serverTime = NetworkManager.ServerTime.Time;
    UpdateServerEvent(serverTime);
    UpdateLocalEvent(serverTime);
}
```

- [ ] **Step 3: 종료 진입점에 서버 권한 가드를 추가**

```csharp
public void StopCurrentEvent()
{
    if (!IsServer)
    {
        return;
    }

    if (_serverEventId == InterferenceEventId.None)
    {
        return;
    }

    InterferenceEventId eventId = _serverEventId;
    double eventEndTime = _serverEventEndTime;
    _serverEventId = InterferenceEventId.None;
    _serverEventEndTime = 0d;
    EndEventRpc(eventId, eventEndTime);
}
```

- [ ] **Step 4: 정적 검증과 Unity 컴파일을 수행**

Run:

```powershell
rg -n "UnityEditor.PackageManager|UnityEngine.LightTransport|LogError.*IsSpawned|enabled = false" Assets/Scripts/Event/InterferenceEventManager.cs
& 'C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe' -batchmode -nographics -quit -projectPath 'C:\Users\admin\Desktop\teams\git' -logFile 'C:\Users\admin\Desktop\teams\git\Logs\pr188-task1-compile.log'
rg -n "error CS|Scripts have compiler errors" Logs/pr188-task1-compile.log
```

Expected: 첫 번째와 마지막 `rg` 모두 매치 없음, Unity 종료 코드 0.

- [ ] **Step 5: 변경을 커밋**

```powershell
git add Assets/Scripts/Event/InterferenceEventManager.cs
git commit -m "fix(#137) : 방해 이벤트 서버 권한과 스폰 가드 보정"
```

---

### Task 2: 이벤트 계약, 상태 조회, 경고 취소 및 종료 사유 추가

**Files:**
- Modify: `Assets/Scripts/Event/IInterferenceEvent.cs`
- Delete: `Assets/Scripts/Event/InterferenceEventId.cs`
- Delete: `Assets/Scripts/Event/InterferenceEventId.cs.meta`
- Modify: `Assets/Scripts/Event/FieldVisionInterferenceEvent.cs`
- Modify: `Assets/Scripts/Event/InterferenceEventManager.cs`

**Interfaces:**
- Produces: `InterferenceEventId`, `InterferenceEndReason`
- Produces: `bool IsWarningActive`, `bool IsActive`, `CancelWarning()`, `Deactivate(InterferenceEndReason reason)`
- Produces: `StopCurrentEvent(InterferenceEndReason reason = InterferenceEndReason.Normal)`

- [ ] **Step 1: enum과 인터페이스를 한 계약 파일에 정의**

`IInterferenceEvent.cs`를 다음 계약으로 교체한다.

```csharp
public enum InterferenceEventId
{
    None,
    FieldVision,
}

public enum InterferenceEndReason
{
    Normal,
    RoundEnded,
    Replaced,
    Despawned,
}

public interface IInterferenceEvent
{
    InterferenceEventId Id { get; }
    bool IsWarningActive { get; }
    bool IsActive { get; }
    void ShowWarning();
    void CancelWarning();
    void Activate();
    void Deactivate(InterferenceEndReason reason);
}
```

그 후 `InterferenceEventId.cs`와 `InterferenceEventId.cs.meta`를 함께 삭제한다.

- [ ] **Step 2: FieldVision 구현을 멱등한 상태 머신으로 변경**

`FieldVisionInterferenceEvent`에 `using System;`을 추가하고 다음 필드와 메서드를 적용한다. 기존 `_onInterferenceEnded`는 씬 호환성을 위해 유지한다.

```csharp
[Serializable]
private sealed class InterferenceEndedEvent : UnityEvent<InterferenceEndReason>
{
}

[SerializeField] private UnityEvent _onWarningCanceled = new UnityEvent();
[SerializeField] private UnityEvent _onInterferenceEnded = new UnityEvent();
[SerializeField] private InterferenceEndedEvent _onInterferenceEndedWithReason = new InterferenceEndedEvent();

public InterferenceEventId Id => InterferenceEventId.FieldVision;
public bool IsWarningActive { get; private set; }
public bool IsActive { get; private set; }

public void ShowWarning()
{
    if (IsWarningActive || IsActive)
    {
        return;
    }

    IsWarningActive = true;
    Debug.Log("[Interference] FieldVision 경고", this);
    _onWarningStarted.Invoke();
}

public void CancelWarning()
{
    if (!IsWarningActive)
    {
        return;
    }

    IsWarningActive = false;
    Debug.Log("[Interference] FieldVision 경고 취소", this);
    _onWarningCanceled.Invoke();
}

public void Activate()
{
    if (IsActive)
    {
        return;
    }

    CancelWarning();
    IsActive = true;
    Debug.Log("[Interference] FieldVision 활성화", this);
    _onInterferenceStarted.Invoke();
}

public void Deactivate(InterferenceEndReason reason)
{
    CancelWarning();

    if (!IsActive)
    {
        return;
    }

    IsActive = false;
    Debug.Log($"[Interference] FieldVision 종료: {reason}", this);
    _onInterferenceEnded.Invoke();
    _onInterferenceEndedWithReason.Invoke(reason);
}
```

- [ ] **Step 3: Manager에서 모든 종료 경로에 명시적 사유를 전달**

```csharp
public override void OnNetworkDespawn()
{
    if (RoundManager.Instance != null)
    {
        RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
    }

    EndLocalEvent(InterferenceEndReason.Despawned);
}

public void StopCurrentEvent(InterferenceEndReason reason = InterferenceEndReason.Normal)
{
    if (!IsServer || _serverEventId == InterferenceEventId.None)
    {
        return;
    }

    InterferenceEventId eventId = _serverEventId;
    double eventEndTime = _serverEventEndTime;
    _serverEventId = InterferenceEventId.None;
    _serverEventEndTime = 0d;
    EndEventRpc(eventId, eventEndTime, reason);
}

[Rpc(SendTo.ClientsAndHost)]
private void EndEventRpc(
    InterferenceEventId eventId,
    double eventEndTime,
    InterferenceEndReason reason)
{
    if (_localEventId != eventId || _localEventEndTime != eventEndTime)
    {
        return;
    }

    EndLocalEvent(reason);
}

private void EndLocalEvent(InterferenceEndReason reason)
{
    if (_localEventId == InterferenceEventId.None)
    {
        return;
    }

    IInterferenceEvent eventToEnd = GetEvent(_localEventId);
    _localEventId = InterferenceEventId.None;
    _localActiveStartTime = 0d;
    _localEventEndTime = 0d;
    _isLocalEventActive = false;

    if (eventToEnd == null)
    {
        return;
    }

    eventToEnd.CancelWarning();

    if (eventToEnd.IsActive)
    {
        eventToEnd.Deactivate(reason);
    }
}
```

호출 사유는 다음처럼 고정한다.

```csharp
// StartEventRpc에서 기존 로컬 이벤트 정리
EndLocalEvent(InterferenceEndReason.Replaced);

// UpdateServerEvent에서 시간 만료
StopCurrentEvent(InterferenceEndReason.Normal);

// UpdateLocalEvent에서 로컬 시각 만료
EndLocalEvent(InterferenceEndReason.Normal);

// HandleRoundStateChanged에서 진행 라운드 이탈
StopCurrentEvent(InterferenceEndReason.RoundEnded);
```

- [ ] **Step 4: 계약 참조와 컴파일을 검증**

Run:

```powershell
rg -n "Deactivate\(\)" Assets/Scripts/Event -g '*.cs'
rg -n "InterferenceEndReason\.(Normal|RoundEnded|Replaced|Despawned)" Assets/Scripts/Event -g '*.cs'
Test-Path Assets/Scripts/Event/InterferenceEventId.cs
& 'C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe' -batchmode -nographics -quit -projectPath 'C:\Users\admin\Desktop\teams\git' -logFile 'C:\Users\admin\Desktop\teams\git\Logs\pr188-task2-compile.log'
rg -n "error CS|Scripts have compiler errors" Logs/pr188-task2-compile.log
```

Expected: 매개변수 없는 `Deactivate()` 매치 없음, 종료 사유 4종 모두 매치, `Test-Path`는 `False`, 컴파일 오류 매치 없음.

- [ ] **Step 5: 변경을 커밋**

```powershell
git add Assets/Scripts/Event/IInterferenceEvent.cs Assets/Scripts/Event/FieldVisionInterferenceEvent.cs Assets/Scripts/Event/InterferenceEventManager.cs
git add -u Assets/Scripts/Event/InterferenceEventId.cs Assets/Scripts/Event/InterferenceEventId.cs.meta
git commit -m "feat(#137) : 방해 이벤트 종료 사유와 상태 계약 추가"
```

---

### Task 3: Manager의 registry와 로컬 수명 주기 책임 분리

**Files:**
- Create: `Assets/Scripts/Event/InterferenceEventRegistry.cs`
- Create: `Assets/Scripts/Event/InterferenceEventRegistry.cs.meta`
- Create: `Assets/Scripts/Event/InterferenceEventLocalController.cs`
- Create: `Assets/Scripts/Event/InterferenceEventLocalController.cs.meta`
- Modify: `Assets/Scripts/Event/InterferenceEventManager.cs`

**Interfaces:**
- Consumes: Task 2의 `IInterferenceEvent`
- Produces: `Register`, `Unregister`, `TryGet`만 제공하는 registry
- Produces: `Start`, `Tick`, `TryEnd`, `End`만 제공하는 local controller

- [ ] **Step 1: 이벤트 검색 책임을 registry로 추출**

`InterferenceEventRegistry.cs`를 다음 내용으로 생성한다.

```csharp
using System.Collections.Generic;

public sealed class InterferenceEventRegistry
{
    private readonly Dictionary<InterferenceEventId, IInterferenceEvent> _events = new();

    public bool Register(IInterferenceEvent interferenceEvent)
    {
        if (interferenceEvent == null || interferenceEvent.Id == InterferenceEventId.None)
        {
            return false;
        }

        _events[interferenceEvent.Id] = interferenceEvent;
        return true;
    }

    public void Unregister(IInterferenceEvent interferenceEvent)
    {
        if (interferenceEvent == null)
        {
            return;
        }

        if (_events.TryGetValue(interferenceEvent.Id, out IInterferenceEvent registeredEvent) &&
            ReferenceEquals(registeredEvent, interferenceEvent))
        {
            _events.Remove(interferenceEvent.Id);
        }
    }

    public bool TryGet(InterferenceEventId eventId, out IInterferenceEvent interferenceEvent)
    {
        return _events.TryGetValue(eventId, out interferenceEvent);
    }
}
```

- [ ] **Step 2: 로컬 수명 주기를 controller로 추출**

`InterferenceEventLocalController.cs`를 다음 내용으로 생성한다.

```csharp
public sealed class InterferenceEventLocalController
{
    private IInterferenceEvent _currentEvent;
    private double _activeStartTime;
    private double _eventEndTime;

    public void Start(
        IInterferenceEvent interferenceEvent,
        double activeStartTime,
        double eventEndTime,
        double currentTime)
    {
        End(InterferenceEndReason.Replaced);
        _currentEvent = interferenceEvent;
        _activeStartTime = activeStartTime;
        _eventEndTime = eventEndTime;

        if (currentTime < activeStartTime)
        {
            _currentEvent.ShowWarning();
        }

        Tick(currentTime);
    }

    public void Tick(double currentTime)
    {
        if (_currentEvent == null)
        {
            return;
        }

        if (currentTime >= _eventEndTime)
        {
            End(InterferenceEndReason.Normal);
            return;
        }

        if (currentTime >= _activeStartTime && !_currentEvent.IsActive)
        {
            _currentEvent.Activate();
        }
    }

    public void TryEnd(
        InterferenceEventId eventId,
        double eventEndTime,
        InterferenceEndReason reason)
    {
        if (_currentEvent == null ||
            _currentEvent.Id != eventId ||
            _eventEndTime != eventEndTime)
        {
            return;
        }

        End(reason);
    }

    public void End(InterferenceEndReason reason)
    {
        if (_currentEvent == null)
        {
            return;
        }

        IInterferenceEvent eventToEnd = _currentEvent;
        _currentEvent = null;
        _activeStartTime = 0d;
        _eventEndTime = 0d;

        eventToEnd.CancelWarning();

        if (eventToEnd.IsActive)
        {
            eventToEnd.Deactivate(reason);
        }
    }
}
```

- [ ] **Step 3: Manager를 두 협력 객체의 조정자로 축소**

Manager의 dictionary와 로컬 상태 필드 4개를 제거하고 다음 필드를 둔다.

```csharp
private readonly InterferenceEventRegistry _registry = new();
private readonly InterferenceEventLocalController _localController = new();
```

등록 API와 RPC/Update 처리는 다음으로 교체한다.

```csharp
public void RegisterEvent(IInterferenceEvent interferenceEvent)
{
    if (!_registry.Register(interferenceEvent))
    {
        Debug.LogWarning("[Interference] 유효하지 않은 이벤트는 등록할 수 없습니다.", this);
    }
}

public void UnregisterEvent(IInterferenceEvent interferenceEvent)
{
    _registry.Unregister(interferenceEvent);
}

[Rpc(SendTo.ClientsAndHost)]
private void StartEventRpc(
    InterferenceEventId eventId,
    double activeStartTime,
    double eventEndTime)
{
    double serverTime = NetworkManager.ServerTime.Time;

    if (serverTime >= eventEndTime)
    {
        return;
    }

    if (!_registry.TryGet(eventId, out IInterferenceEvent interferenceEvent))
    {
        Debug.LogError($"[Interference] {eventId} 이벤트 구현체가 등록되지 않았습니다.", this);
        return;
    }

    _localController.Start(interferenceEvent, activeStartTime, eventEndTime, serverTime);
}

[Rpc(SendTo.ClientsAndHost)]
private void EndEventRpc(
    InterferenceEventId eventId,
    double eventEndTime,
    InterferenceEndReason reason)
{
    _localController.TryEnd(eventId, eventEndTime, reason);
}
```

`Update()`는 `_localController.Tick(serverTime)`, `OnNetworkDespawn()`은 `_localController.End(InterferenceEndReason.Despawned)`를 호출한다. 기존 `UpdateLocalEvent`, `EndLocalEvent`, `GetEvent` 메서드는 삭제한다.

- [ ] **Step 4: Unity가 새 스크립트의 meta 파일을 생성하도록 import 후 컴파일**

Run:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe' -batchmode -nographics -quit -projectPath 'C:\Users\admin\Desktop\teams\git' -logFile 'C:\Users\admin\Desktop\teams\git\Logs\pr188-task3-compile.log'
Test-Path Assets/Scripts/Event/InterferenceEventRegistry.cs.meta
Test-Path Assets/Scripts/Event/InterferenceEventLocalController.cs.meta
rg -n "error CS|Scripts have compiler errors" Logs/pr188-task3-compile.log
rg -n "Dictionary|_localEventId|_localActiveStartTime|_localEventEndTime|_isLocalEventActive|UpdateLocalEvent|EndLocalEvent|GetEvent" Assets/Scripts/Event/InterferenceEventManager.cs
```

Expected: meta 확인은 모두 `True`, 컴파일 오류와 제거 대상 책임은 매치 없음.

- [ ] **Step 5: 변경을 커밋**

```powershell
git add Assets/Scripts/Event/InterferenceEventRegistry.cs Assets/Scripts/Event/InterferenceEventRegistry.cs.meta
git add Assets/Scripts/Event/InterferenceEventLocalController.cs Assets/Scripts/Event/InterferenceEventLocalController.cs.meta
git add Assets/Scripts/Event/InterferenceEventManager.cs
git commit -m "refactor(#137) : 방해 이벤트 로컬 수명 주기 책임 분리"
```

---

### Task 4: Multiplayer Play Mode 회귀 검증

**Files:**
- Verify: `Assets/Scenes/PlayScene.unity`
- Verify: `Assets/Scripts/Event/*.cs`

**Interfaces:**
- Consumes: Task 1~3의 완료 상태
- Produces: 각 리뷰 피드백을 닫을 수 있는 실행 증거

- [ ] **Step 1: 최종 컴파일과 변경 범위를 검증**

Run:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe' -batchmode -nographics -quit -projectPath 'C:\Users\admin\Desktop\teams\git' -logFile 'C:\Users\admin\Desktop\teams\git\Logs\pr188-final-compile.log'
rg -n "error CS|Scripts have compiler errors" Logs/pr188-final-compile.log
git diff main...HEAD -- Assets/Scripts/Event Assets/Scenes/PlayScene.unity
```

Expected: 컴파일 오류 없음. 씬에는 의도하지 않은 YAML 변경이 없고 Event 스크립트 및 대응 meta만 변경된다.

- [ ] **Step 2: 스폰 전 로그와 중복 manager를 확인**

Unity Editor에서 `PlayScene`을 열고 Console을 비운 뒤 Play한다.

1. 네트워크 스폰 전 대기 중 `Manager가 아직 Spawn되지 않았습니다` 오류가 매 프레임 발생하지 않는지 확인한다.
2. 런타임에 `InterferenceEventManager` GameObject를 하나 복제한다.
3. 다음 프레임에 복제본이 파괴되고 원본 `Instance`만 남는지 확인한다.

Expected: 반복 오류 0건, 중복 GameObject 0개.

- [ ] **Step 3: 정상 종료와 중복 호출 방지를 확인**

Multiplayer Play Mode에서 Host 1개와 Client 1개를 실행한다.

1. Host에서 Round1/2 상태 중 F2를 한 번 누른다.
2. Host와 Client에서 `경고` 로그가 각 1회 발생하는지 확인한다.
3. 경고 시간 후 `활성화` 로그가 각 1회 발생하는지 확인한다.
4. 5~15초 후 `FieldVision 종료: Normal` 로그가 각 1회 발생하는지 확인한다.
5. 효과 종료 뒤 추가 `Deactivate`/종료 로그가 없는지 확인한다.

Expected: warning → active → `Normal` end 순서가 각 피어에서 정확히 한 번씩 발생한다.

- [ ] **Step 4: 강제 종료 사유와 경고 취소를 확인**

1. Host에서 F2를 누른 직후 라운드를 `Round1Clear`, `Fail`, 또는 `Success`로 전환한다.
2. Host와 Client에서 `경고 취소`가 발생하고 활성화가 뒤늦게 실행되지 않는지 확인한다.
3. 다시 이벤트를 활성화한 뒤 라운드를 종료한다.
4. Host와 Client에서 `FieldVision 종료: RoundEnded`가 각 1회인지 확인한다.
5. 실행 중 NetworkObject를 despawn하고 로컬 정리가 `Despawned` 경로를 타는지 확인한다.

Expected: 경고 중 종료는 warning만 취소하고, 활성 중 종료는 사유를 포함해 한 번만 종료한다.

- [ ] **Step 5: 클라이언트 권한과 replacement 정리를 확인**

1. Client에서 F2를 눌러 `TryStartEvent`가 서버 전용 경고와 함께 `false`를 반환하는지 확인한다.
2. Client 측에서 `StopCurrentEvent()`를 호출해 서버 상태 변경이나 RPC 권한 예외가 없는지 확인한다.
3. 새 시작 RPC가 기존 로컬 효과 위에 도착하는 개발 시나리오를 실행해 기존 효과가 `Replaced`로 정리된 뒤 새 효과가 시작되는지 확인한다.

Expected: 클라이언트는 서버 상태를 변경하지 않고, 로컬 replacement는 중첩 없이 정리된다.

- [ ] **Step 6: 최종 상태를 기록**

Run:

```powershell
git status --short
git log -3 --oneline
```

Expected: 의도한 Event 스크립트/meta 외 미커밋 변경이 없고, Task 1~3의 커밋 3개가 순서대로 보인다.

## Review Thread Closure Mapping

- import 삭제 2개: Task 1 정적 검색 결과로 답변
- `!IsSpawned` 반복 로그: Task 1 및 Task 4 Step 2 결과로 답변
- 서버 가드: Task 1 및 Task 4 Step 5 결과로 답변
- duplicate Destroy: Task 1 및 Task 4 Step 2 결과로 답변
- enum 배치/종료 사유: Task 2 계약 파일과 4개 종료 경로로 답변
- 인터페이스 구체화: Task 2의 상태/취소/사유 API로 답변
- Manager 책임 분리: Task 3의 registry/controller 추출과 제거된 필드/메서드 검색 결과로 답변

