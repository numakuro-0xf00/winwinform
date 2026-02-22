# WSL + Git Worktree 環境での `git push` 失敗問題

## ステータス

**解決済み** — 2026-02-22 発見、同日対応完了。`gh auth setup-git` により credential helper を WSL ネイティブに切り替え。

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

---

## 適用した対応

### `gh auth setup-git` による credential helper 切り替え（方針A-1）

```bash
gh auth setup-git
```

これにより `~/.gitconfig` に以下が追加された:

```ini
[credential "https://github.com"]
	helper =
	helper = !/usr/bin/gh auth git-credential
[credential "https://gist.github.com"]
	helper =
	helper = !/usr/bin/gh auth git-credential
```

**ポイント**:
- GitHub 用のみ gh CLI に切り替わり、Azure DevOps の設定（Windows GCM）はそのまま維持
- 空の `helper =` 行がグローバル設定の Windows GCM をこのホストに対して無効化
- その後の `helper = !/usr/bin/gh auth git-credential` が WSL ネイティブの認証を提供

### 検証結果

```bash
# worktree 内からの git push --dry-run が成功
$ cd .claude/worktrees/hazy-wondering-eich
$ git push --dry-run -u origin worktree-hazy-wondering-eich
To https://github.com/numakuro-0xf00/winwinform.git
 * [new branch]      worktree-hazy-wondering-eich -> worktree-hazy-wondering-eich
```

---

## 不採用とした方針

### 方針B: ワークツリーのパスを Windows パスに変換する

gitdir を Windows パスにすると git のローカル操作が壊れる可能性があるため非推奨。

### 方針C: push 時のみ credential helper を一時切り替え

```bash
git -c credential.helper="!gh auth git-credential" push origin <branch>
```

根本解決ではないため、方針A-1 を適用後は不要。

---

## 関連情報

- [Git Credential Manager - WSL issues](https://github.com/git-ecosystem/git-credential-manager/blob/main/docs/wsl.md)
- Claude Code ワークツリー: `.claude/worktrees/` 配下に作成される
- 本リポジトリのワークフロー: `CLAUDE.md` の Git Workflow セクション参照
