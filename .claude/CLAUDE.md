# Undercover 프로젝트 가이드

Unity 게임 프로젝트. 아래 컨벤션을 지켜서 작업한다.

## Git 워크플로우

- `main`에 **직접 커밋·머지하지 않는다**. 항상 작업 브랜치 → PR → main 흐름을 사용한다.
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
```
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
```
feat(#12) : 변장 시스템 추가
fix(#13) : 탐지 UI 크래시 수정
asset(#14) : 경비병 효과음 추가
balance(#15) : 스테이지 보상 수치 조정
docs(#16) : 조작법 문서 추가
refactor(#17) : AI 상태 제어 구조 정리
chore(#18) : 빌드 설정 정리
```

예외: 이슈 번호가 없거나 명확하지 않은 경우 `fix: Firebase 로그 메시지 정리`처럼 쓴 기록도 있다.

---

# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.
