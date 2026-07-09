# .codex

`.claude` 폴더의 프로젝트 지침과 보조 명령을 Codex에서 참고할 수 있도록 대응시킨 폴더다.

## 파일 대응표

| Claude | Codex | 비고 |
| --- | --- | --- |
| `.claude/CLAUDE.md` | `.codex/AGENTS.md` | 프로젝트 규칙과 작업 원칙을 Codex용으로 정리 |
| `.claude/GDD.md` | `.codex/GDD.md` | 게임 디자인 문서 원문 복제 |
| `.claude/commands/issue.md` | `.codex/commands/issue.md` | 이슈 작성 프롬프트 템플릿 |
| `.claude/settings.local.json` | `.codex/settings.local.json` | 원본 Claude 권한 설정 보존용 사본 |
| `.claude/settings.local.json` | `.codex/permissions.md` | Claude 전용 권한 설정을 Codex 승인 모델 기준의 참고 문서로 변환 |

## 사용 방법

- 프로젝트 맥락이 필요하면 `.codex/AGENTS.md`와 `.codex/GDD.md`를 먼저 읽는다.
- Codex가 자동으로 참고할 프로젝트 지침은 루트 `AGENTS.md`에 둔다.
- 사용자가 "이슈로 만들어줘"라고 요청하면 `.codex/commands/issue.md` 절차를 따른다.
- 외부 도구나 저장소 밖 파일이 필요하면 `.codex/permissions.md`를 보고 필요한 승인 범위를 판단한다.
