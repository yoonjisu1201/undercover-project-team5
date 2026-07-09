# Undercover 프로젝트 가이드

Unity 게임 프로젝트. Codex로 작업할 때 아래 컨벤션을 지킨다.

## 우선 참고 문서

- 게임 기획과 시스템 맥락은 `.codex/GDD.md`를 우선 참고한다.
- Claude용 원본 설정은 `.claude/`에 있으며, 이 폴더는 가능한 범위에서 Codex용으로 대응한 사본이다.
- 이슈 작성 흐름은 `.codex/commands/issue.md`를 사용한다.

## Git 워크플로우

- `main`에 직접 커밋하거나 머지하지 않는다. 항상 작업 브랜치 -> PR -> main 흐름을 사용한다.
- 커밋 전 `git log --oneline`으로 기존 형식을 확인한다.
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

## 이슈 생성 시 Project / Milestone / Iteration

`gh issue create`로 제목·라벨·본문만 채우고 끝내지 않는다. 이슈를 생성하면 아래 세 가지를 항상 함께 설정한다.

- **Project**: `@UnderCover project - team5` (프로젝트 번호 1, owner `yoonjisu1201`)
- **Milestone**: `BuildN` 마일스톤 중 현재 날짜가 속한 빌드를 기본값으로 사용한다.
- **Iteration**: 프로젝트의 Iteration 필드(예: "1주차 (빌드 목요일)", "2주차", ...) 중 현재 날짜가 속한 주차를 기본값으로 사용한다. Milestone과 같은 기간을 가리켜야 한다.

`gh issue create`는 `--milestone`, `--project`까지만 지정할 수 있다. Iteration은 생성 후 `gh project item-edit`으로 별도 설정해야 한다.

## Codex 작업 원칙

### 1. 작업 전 확인

- 모호한 요구사항은 가정을 명시하고, 위험한 가정이면 사용자에게 짧게 확인한다.
- 여러 해석이 가능한 경우 조용히 하나를 고르지 말고 차이를 설명한다.
- 더 단순한 접근이 있다면 먼저 제안한다.

### 2. 단순하게 구현

- 요청받지 않은 기능을 추가하지 않는다.
- 한 번만 쓰이는 코드에 과한 추상화를 만들지 않는다.
- 요구되지 않은 설정화·확장성을 넣지 않는다.
- 불가능한 시나리오까지 방어하느라 코드를 키우지 않는다.

### 3. 필요한 범위만 수정

- 기존 코드의 스타일과 패턴을 따른다.
- 관련 없는 코드, 주석, 포맷팅을 정리하지 않는다.
- 사용자 요청과 직접 연결되는 줄만 변경한다.
- 변경으로 인해 새로 생긴 미사용 import, 변수, 함수는 정리한다.
- 기존에 있던 죽은 코드는 요청받지 않았다면 삭제하지 않고 필요 시 언급만 한다.

### 4. 검증까지 수행

- 구현 목표를 확인 가능한 기준으로 바꾼다.
- 버그 수정은 가능하면 재현 확인 또는 테스트로 검증한다.
- 변경 후 Unity 프로젝트 특성에 맞는 확인 방법을 실행하거나, 실행하지 못한 이유를 사용자에게 말한다.

