# WSL + Git Worktree 環境での `git push` 失敗問題

## ステータス

**未解決** — 2026-02-22 発見。本ドキュメントで原因分析と対応方針を記録する。

---

## 現象

Claude Code のワークツリー機能で作成されたブランチから `git push` を実行すると、認証に失敗する。

```
$ git push -u origin worktree-architecture-review-fixes

fatal: not a git repository: /mnt/c/Users/home/source/repos/winwinform/.git/worktrees/architecture-review-fixes

System.InvalidOperationException: StandardError がリダイレクトされていません。
   場所 GitCredentialManager.GitProcess.CreateGitException(...)
   ...
fatal: could not read Username for 'https://github.com': No such device or address
```

`git commit` 等のローカル操作は正常に動作する。`git push` のみ失敗する。

---

## 影響範囲

- **Claude Code のワークツリー機能**（`/worktree` コマンド、`EnterWorktree` ツール、Task の `isolation: "worktree"` オプション）で作成された全ブランチからの `git push` が不可能
- Claude Code の Git ワークフロー（`CLAUDE.md` に記載のブランチ作成 → 作業 → push → PR 作成）が worktree 内で完結できない
- 通常のブランチ操作（worktree を使わない場合）には影響なし

---

## 原因分析

### 環境構成

```
Host OS:     Windows
WSL:         Linux 6.6.87.2-microsoft-standard-WSL2
Git (WSL):   credential.helper = /mnt/c/Program Files/Git/mingw64/bin/git-credential-manager.exe
リポジトリ:   /mnt/c/Users/home/source/repos/winwinform/ (NTFS on /mnt/c)
```

### ワークツリーのファイル構造

```
メインリポジトリ:
  /mnt/c/Users/home/source/repos/winwinform/
    .git/                          ← 通常の git ディレクトリ
    .git/worktrees/architecture-review-fixes/
      HEAD                         ← ワークツリーの HEAD
      gitdir                       ← ワークツリーの .git ファイルへの逆参照
      commondir                    ← "../.." (メインの .git への相対パス)

ワークツリー:
  /mnt/c/Users/home/source/repos/winwinform/.claude/worktrees/architecture-review-fixes/
    .git                           ← ファイル（ディレクトリではない）
    内容: "gitdir: /mnt/c/Users/home/source/repos/winwinform/.git/worktrees/architecture-review-fixes"
```

### 失敗メカニズム

1. WSL 上の `git push` が認証情報を要求
2. credential.helper に設定された **Windows 側の GCM**（`git-credential-manager.exe`）が呼び出される
3. GCM は自身の初期化時に `git config` を読むため、**Windows プロセスとして** git リポジトリのパスを解決しようとする
4. GCM に渡される CWD またはリポジトリパスが `/mnt/c/.../.git/worktrees/architecture-review-fixes` — これは **WSL パス形式**
5. Windows プロセスである GCM は WSL パス (`/mnt/c/...`) を解決できない
6. `fatal: not a git repository` エラーで GCM が異常終了
7. 認証情報を取得できず `git push` が失敗

### 根本原因

**WSL の Git が Windows 側の Git Credential Manager (GCM) を credential helper として使用している** 環境で、**git worktree** のパス解決が WSL ↔ Windows 間のパス変換に失敗する。

通常のリポジトリ（worktree でない）からの push は成功するため、以下の条件の**組み合わせ**が原因:

1. credential.helper が Windows 側のバイナリ (`/mnt/c/Program Files/Git/mingw64/bin/git-credential-manager.exe`)
2. git worktree の gitdir が WSL パス形式
3. GCM が内部で git コマンドを実行する際に worktree のパスを Windows パスに変換できない

### 再現確認

```bash
# GCM 単体でも同じエラーが再現する（worktree ディレクトリをCWDにして実行）
$ cd /mnt/c/Users/home/source/repos/winwinform/.claude/worktrees/architecture-review-fixes
$ /mnt/c/Program\ Files/Git/mingw64/bin/git-credential-manager.exe --version
fatal: not a git repository: .../worktrees/architecture-review-fixes
```

---

## 対応方針

### 方針A: WSL 側に credential helper を切り替える（推奨）

WSL の git が Windows 側の GCM を使わず、**WSL ネイティブの credential 管理**を使うようにする。

#### A-1: `gh auth setup-git` を使用

```bash
# gh CLI の credential helper を設定
gh auth setup-git
```

これにより `~/.gitconfig` の credential.helper が `!/usr/bin/gh auth git-credential` に変更される。gh CLI は WSL ネイティブで動作するため worktree パスの問題が発生しない。

#### A-2: git-credential-store を使用

```bash
git config --global credential.helper store
# 初回 push 時にユーザー名/トークンを入力 → ~/.git-credentials に保存
```

#### 設定変更時の注意

- `~/.gitconfig` の既存の credential.helper 行を置換する必要がある
- Azure DevOps (`credential.https://dev.azure.com.usehttppath=true`) の設定も gh CLI 経由で動作するか確認が必要

### 方針B: ワークツリーのパスを Windows パスに変換する

GCM が解決可能な Windows パスで worktree の gitdir を記述する。

```bash
# WSL パス → Windows パス変換
wslpath -w /mnt/c/Users/home/source/repos/winwinform/.git/worktrees/architecture-review-fixes
# → C:\Users\home\source\repos\winwinform\.git\worktrees\architecture-review-fixes
```

ただし、git 自体が WSL パスで動作しているため、gitdir を Windows パスにすると git のローカル操作が壊れる可能性がある。**この方針は非推奨**。

### 方針C: push 時のみ credential helper を一時切り替え

```bash
git -c credential.helper="!gh auth git-credential" push origin <branch>
```

根本解決ではないが、現在のコミットを push する即時回避策として使用可能。

---

## 推奨対応手順

1. **即時対応**: 方針C で現在のコミット済みブランチ `worktree-architecture-review-fixes` を push
2. **根本対応**: 方針A-1 (`gh auth setup-git`) で credential helper を WSL ネイティブに切り替え
3. **検証**: worktree 内からの `git push` が正常動作することを確認
4. **ドキュメント更新**: 問題解消後、本ドキュメントのステータスを更新

---

## 関連情報

- [Git Credential Manager - WSL issues](https://github.com/git-ecosystem/git-credential-manager/blob/main/docs/wsl.md)
- Claude Code ワークツリー: `.claude/worktrees/` 配下に作成される
- 本リポジトリのワークフロー: `CLAUDE.md` の Git Workflow セクション参照

---

## 未 push のコミット

ブランチ `worktree-architecture-review-fixes` に以下のコミットが push 待ち:

```
18dbe6a 設計レビュー指摘事項の対応（A-1〜A-4, B-1〜B-7, C-1/C-2/C-6）
```

内容: `doc/architecture-review.md` の指摘に基づく設計ドキュメント・csproj の修正。詳細はコミットメッセージ参照。
