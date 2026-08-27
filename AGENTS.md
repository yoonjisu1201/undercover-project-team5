# Undercover Codex 작업 가이드

Unity 게임 프로젝트. Codex로 작업할 때는 이 문서를 우선 지침으로 삼는다.

## 프로젝트 맥락

- 게임 기획과 시스템 의도는 `.codex/GDD.md`를 참고한다.
- Claude용 원본 지침은 `.claude/CLAUDE.md`에 있다.
- Codex용 보조 자료는 `.codex/`에 있다.
- 사용자가 "이슈로 만들어줘"라고 요청하면 `.codex/commands/issue.md`의 절차를 따른다.

## Git 워크플로우

- `main`에 직접 커밋하거나 머지하지 않는다. 항상 작업 브랜치 -> PR -> main 흐름을 사용한다.
- 커밋 전 `git log --oneline`으로 기존 형식을 확인한다.
- 커밋 메시지를 제안하거나 작성할 때는 먼저 `git branch --show-current`으로 브랜치명에 포함된 이슈 번호를 확인하고, `git log --oneline`으로 최근 형식을 확인한다. 이슈 번호 유무·공백·언어 표기까지 저장소 관례에 맞춘다.
- PR을 생성하거나 수정할 때는 먼저 `.github/PULL_REQUEST_TEMPLATE.md`와 최근 유사 PR 본문을 확인하고, 모든 템플릿 섹션과 `close #이슈번호`를 저장소 관례에 맞춰 작성한다.
- 여러 작업이 섞였으면 한 번에 커밋하지 말고 작업 단위별로 나눈다.
- 세부 설명이 필요하면 what보다 why 중심으로 쓴다.

## 타입 정의

| 이슈 템플릿 | 이슈 제목 prefix | 브랜치 type | 커밋 type | 의미 |
| --- | --- | --- | --- | --- |
| Feature | `[FEAT]` | `feat` | `feat` | 기능 추가 |
| Bug Report | `[BUG]` | `fix` | `fix` | 버그 수정 |
| Asset Request | `[ASSET]` | `asset` | `asset` | 이미지·사운드 등 에셋 작업 |
| Balance Tuning | `[BALANCE]` | `balance` | `balance` | 수치·밸런스 조정 |
| Documentation | `[DOCS]` | `docs` | `docs` | 문서 작업 |
| Refactor | `[REFACTOR]` | `refactor` | `refactor` | 동작 변경 없는 구조 개선 |
| Chore | `[CHORE]` | `chore` | `chore` | 설정·빌드·유지보수 작업 |

## 브랜치 작명

형식: `{type}/{issue-number}-{short-kebab-name}`

- 가능하면 이슈 번호를 포함한다.
- 이슈 제목의 핵심 문구를 영어 kebab-case로 줄여 쓴다.

예시:

```text
feat/12-player-disguise-system
fix/13-detection-ui-crash
asset/14-guard-sfx
balance/15-stage-reward-tuning
docs/16-control-guide
refactor/17-ai-state-controller
chore/18-build-settings
```

## 커밋 메시지

형식: `type(#issue-number) : 한글 요약`

예시:

```text
feat(#12) : 변장 시스템 추가
fix(#13) : 탐지 UI 크래시 수정
asset(#14) : 경비병 효과음 추가
balance(#15) : 스테이지 보상 수치 조정
docs(#16) : 조작법 문서 추가
refactor(#17) : AI 상태 제어 구조 정리
chore(#18) : 빌드 설정 정리
```

예외: 이슈 번호가 없거나 명확하지 않은 경우 `fix: Firebase 로그 메시지 정리`처럼 쓴 기록도 있다.

## 이슈 작성

사용자가 이슈 생성을 요청하면 바로 GitHub에 만들지 않는다.

1. 사용자 입력을 바탕으로 타입을 고른다.
2. `.github/ISSUE_TEMPLATE` 형식에 맞춰 제목과 본문 초안을 작성한다.
3. 사용자에게 초안을 보여주고 확인을 받는다.
4. 사용자가 승인하면 `gh issue create`로 생성한다.
5. 생성한 이슈에 GitHub Project + Milestone + Iteration을 설정한다.

애매한 경우에는 한두 가지 핵심 질문만 한다. 없는 정보를 임의로 지어내지 않는다.

### Project / Milestone / Iteration

이슈를 생성하면 제목·라벨·본문만 채우고 끝내지 않는다.

- **Project**: `@UnderCover project - team5` (프로젝트 번호 1, owner `yoonjisu1201`)
- **Milestone**: `BuildN` 마일스톤 중 현재 날짜가 속한 빌드를 기본값으로 사용한다.
- **Iteration**: 프로젝트의 Iteration 필드(예: "1주차 (빌드 목요일)", "2주차", ...) 중 현재 날짜가 속한 주차를 기본값으로 사용한다. Milestone과 같은 기간을 가리켜야 한다.

`gh issue create`는 `--milestone`, `--project`까지만 지정할 수 있다. Iteration은 생성 후 `gh project item-edit`으로 별도 설정해야 한다.

## Codex 작업 원칙

### 1. 코딩 전 생각하기

- 모호한 요구사항은 가정을 명시한다.
- 위험하거나 되돌리기 어려운 가정이면 사용자에게 짧게 확인한다.
- 여러 해석이 가능하면 차이를 설명하고, 필요한 경우 선택지를 제시한다.
- 더 단순한 접근이 있으면 먼저 말한다.

### 2. 단순하게 구현하기

- 요청받지 않은 기능을 추가하지 않는다.
- 한 번만 쓰이는 코드에 과한 추상화를 만들지 않는다.
- 요구되지 않은 설정화나 확장성을 넣지 않는다.
- 불가능한 시나리오까지 방어하느라 코드를 키우지 않는다.
- 같은 문제를 훨씬 짧고 명확하게 풀 수 있으면 단순한 쪽을 선택한다.

### 3. 필요한 범위만 수정하기

- 기존 코드의 스타일과 패턴을 따른다.
- 관련 없는 코드, 주석, 포맷팅을 정리하지 않는다.
- 사용자 요청과 직접 연결되는 줄만 변경한다.
- 변경으로 인해 새로 생긴 미사용 import, 변수, 함수는 정리한다.
- 기존에 있던 죽은 코드는 요청받지 않았다면 삭제하지 않고 필요 시 언급만 한다.

### 4. 목표 기준으로 검증하기

- 구현 목표를 확인 가능한 기준으로 바꾼다.
- 버그 수정은 가능하면 재현 확인 또는 테스트로 검증한다.
- 기능 추가는 최소한의 동작 확인 방법을 함께 생각한다.
- 변경 후 Unity 프로젝트 특성에 맞는 확인 방법을 실행하거나, 실행하지 못한 이유를 사용자에게 말한다.

## Unity 작업 주의

- `Assets/`, `Packages/`, `ProjectSettings/`의 기존 구조와 네이밍을 따른다.
- Unity가 생성하는 `Library/`, `Temp/`, `Logs/`, `UserSettings/` 파일은 일반적으로 수정하지 않는다.
- `.meta` 파일은 에셋 추적에 중요하므로, 에셋을 추가·이동·삭제할 때 함께 고려한다.
- 씬, 프리팹, ScriptableObject 변경은 영향 범위를 특히 조심해서 확인한다.

