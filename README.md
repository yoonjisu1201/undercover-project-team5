# 미확인 개체 대응반 (Undercover)

> 본부와 현장으로 나뉜 팀이 군중 속에 위장한 외계인을 제한시간 안에 식별·포획하는 **협동 추리 파티 게임**

3~4인이 함께 플레이하며, 한 팀은 **본부(관제)**, 다른 팀은 **현장(조사)**을 맡습니다. 본부는 CCTV·힌트를 보고 말로 설명하고, 현장은 군중 속 후보를 직접 확인합니다. 제한된 단서에서 출발해 조사로 힌트를 열고, 말 전달의 혼선과 방해를 넘기며 외계인을 찾아냅니다.

자세한 기획은 [게임 디자인 문서(GDD)](.claude/GDD.md)를 참고하세요.

## 게임 개요

| 항목 | 내용 |
| --- | --- |
| 장르 | 협동 추리 파티 게임 / 외계인 위장 식별 |
| 플레이 인원 | 3~4인 (멀티플레이) |
| 한 판 길이 | 15~20분 (목표) |
| 승리 조건 | 제한시간 내 외계인 후보 확정 → 포획 성공 |
| 플랫폼 | PC |

## 기술 스택

- **엔진**: Unity `6000.3.15f1` (Unity 6)
- **렌더 파이프라인**: Universal Render Pipeline (URP) 17.3.0
- **입력**: Input System 1.19.0
- **주요 패키지**: AI Navigation, Multiplayer Center, Timeline, uGUI

## 시작하기

### 요구 사항
- [Unity Hub](https://unity.com/download)
- Unity Editor **6000.3.15f1** (동일 버전 권장)
- Git

### 설치
```bash
git clone https://github.com/yoonjisu1201/undercover-project-team5.git
```
1. Unity Hub → `Add` → 클론한 폴더 선택
2. Editor 버전 `6000.3.15f1`로 프로젝트 열기
3. 최초 실행 시 패키지 임포트 및 라이브러리 빌드까지 대기

> `Library/`, `Temp/`, `Logs/` 등은 git에 포함되지 않으며 Unity가 자동 생성합니다.

## 폴더 구조

```
Undercover/
├── Assets/            # 게임 에셋 · 씬 · 스크립트
│   ├── Scenes/
│   ├── Settings/      # URP 렌더 세팅
│   └── ...
├── Packages/          # 패키지 매니페스트
├── ProjectSettings/   # 프로젝트 설정
├── Docs/              # 기획서 원본
└── .github/           # 이슈 · PR 템플릿
```

## 협업 컨벤션

브랜치·커밋·이슈 규칙은 아래를 따릅니다.

- **브랜치**: `{type}/{issue-number}-{short-kebab-name}` (예: `feat/12-player-disguise-system`)
- **커밋**: `type(#issue-number) : 한글 요약` (예: `feat(#12) : 변장 시스템 추가`)
- **워크플로우**: `main`에 직접 커밋 금지 → 작업 브랜치 → PR → main

전체 컨벤션은 [.claude/CLAUDE.md](.claude/CLAUDE.md)에서 확인하세요. 이슈 생성 시 `.github/ISSUE_TEMPLATE`의 템플릿을 사용합니다.

## 팀

**경일특공대** (Team 5)

- 윤지수
- 정호종
- 남현우
- 박우빈
