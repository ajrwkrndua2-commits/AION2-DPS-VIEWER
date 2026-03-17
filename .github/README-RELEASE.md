GitHub Actions 자동 릴리즈 사용 방법

1. 버전을 코드에 반영합니다.
2. 커밋 후 원격에 push 합니다.
3. 태그를 만듭니다.
   예: v4.0.16
4. 태그를 push 합니다.

예시 명령:

```powershell
git add .
git commit -m "Release version 4.0.16"
git push
git tag v4.0.16
git push origin v4.0.16
```

동작:
- GitHub Actions가 자동으로 실행됩니다.
- 개발판/배포판을 빌드합니다.
- zip 파일을 생성합니다.
- GitHub Releases에는 배포판 zip만 자동 업로드합니다.
- 개발판은 릴리즈 첨부 파일에는 올리지 않습니다.
