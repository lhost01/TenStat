# 徽章（Shields.io）与 GitHub 上传说明

README 顶部已经放好一组**静态徽章**（不依赖仓库，复制即可显示）。这个文件是「徽章源码 + 想改文案/换样式/加仓库数据」的备查页，以及上传 GitHub 前的检查清单。

---

## 1. 现在用的徽章（直接复制）

### 第一行：技术栈

```markdown
<p align="center">
  <a href="#"><img alt="Platform" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square&logo=windows11&logoColor=white"></a>
  <a href="#"><img alt=".NET" src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet&logoColor=white"></a>
  <a href="#"><img alt="WinUI 3" src="https://img.shields.io/badge/UI-WinUI%203%20%2F%20Fluent%202-0067C0?style=flat-square&logo=windows&logoColor=white"></a>
  <a href="#"><img alt="C#" src="https://img.shields.io/badge/C%23-12-239120?style=flat-square&logo=csharp&logoColor=white"></a>
  <a href="#"><img alt="SQLite" src="https://img.shields.io/badge/Storage-SQLite-003B57?style=flat-square&logo=sqlite&logoColor=white"></a>
  <a href="#"><img alt="Live2D" src="https://img.shields.io/badge/Desktop%20Pet-Live2D-FF5C8A?style=flat-square"></a>
</p>
```

### 第二行：隐私 / 许可 / 版本

```markdown
<p align="center">
  <a href="#"><img alt="Offline" src="https://img.shields.io/badge/network-100%25%20offline-success?style=flat-square"></a>
  <a href="#"><img alt="No telemetry" src="https://img.shields.io/badge/telemetry-none-brightgreen?style=flat-square"></a>
  <a href="#"><img alt="Privacy" src="https://img.shields.io/badge/privacy-no%20input%20content-0F7B0F?style=flat-square"></a>
  <a href="#"><img alt="Admin" src="https://img.shields.io/badge/admin-not%20required-informational?style=flat-square"></a>
  <a href="#"><img alt="License" src="https://img.shields.io/badge/license-MIT-3DA639?style=flat-square"></a>
  <a href="#"><img alt="Version" src="https://img.shields.io/badge/version-v2.1.0-blue?style=flat-square"></a>
</p>
```

---

## 2. 仓库相关徽章（需要把 `OWNER/REPO` 换掉）

```markdown
<a href="https://github.com/OWNER/REPO/releases"><img alt="Release" src="https://img.shields.io/github/v/release/OWNER/REPO?style=flat-square&logo=github"></a>
<a href="https://github.com/OWNER/REPO/stargazers"><img alt="Stars" src="https://img.shields.io/github/stars/OWNER/REPO?style=flat-square&logo=github"></a>
<a href="https://github.com/OWNER/REPO/issues"><img alt="Issues" src="https://img.shields.io/github/issues/OWNER/REPO?style=flat-square&logo=github"></a>
<a href="https://github.com/OWNER/REPO"><img alt="Last commit" src="https://img.shields.io/github/last-commit/OWNER/REPO?style=flat-square&logo=github"></a>
<a href="https://github.com/OWNER/REPO/blob/main/LICENSE"><img alt="License" src="https://img.shields.io/github/license/OWNER/REPO?style=flat-square"></a>
```

> ⚠️ 没替换就直接用，会渲染成 `invalid` / `not found`，所以 README 里默认把它们注释掉了。

---

## 3. 自己改徽章

Shields 的静态徽章格式：

```
https://img.shields.io/badge/<标签>-<内容>-<颜色>?style=<样式>&logo=<图标>&logoColor=<图标色>
```

- **空格**写 `%20`，**短横线**写 `--`（两个），**下划线**写 `__`（两个）；
- 颜色可以是名字（`blue` `green` `brightgreen` `success` `important` `critical` `inactive` …）或十六进制（`FF5C8A`，不带 `#`）；
- 样式：`flat`（默认）、`flat-square`（本项目用的）、`plastic`、`for-the-badge`、`social`；
- 图标名来自 [Simple Icons](https://simpleicons.org/)（`windows11`、`dotnet`、`csharp`、`sqlite`、`github` …）。

例子：把版本号换成 v2.1.0

```
https://img.shields.io/badge/version-v2.1.0-blue?style=flat-square
```

改成 `for-the-badge` 的大号样式（适合做 hero 区）：

```markdown
<img alt="Platform" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=for-the-badge&logo=windows11&logoColor=white">
```

---

## 4. 上传 GitHub 前要做的几件事

### 4.1 别把产物传上去

`.gitignore` 已经包含：

```
bin/  obj/  .vs/          # 编译中间物
dist/  *.zip              # publish.ps1 的产物（99 MB / 328 个文件）
build/*.png               # 调试截图
*.db *.db-wal *.db-shm    # 本地统计库
```

`dist/` 和 `TenStat-win-x64.zip` **走 Releases 而不是提交进仓库**：
单文件限制 100 MB，仓库超过 1 GB 会警告，而且二进制进 Git 历史后很难清。

### 4.2 建议的首次提交

```powershell
cd F:\活动记录器
git init
git add .
git status          # 确认没有 dist/、bin/、obj/、*.zip
git commit -m "feat: TenStat v2.1.0（WinUI 3 + 全局TenStat + 透明 Live2D 桌宠）"
git branch -M main
git remote add origin https://github.com/OWNER/REPO.git
git push -u origin main
```

### 4.3 发一个 Release 挂可执行文件

1. GitHub → Releases → Draft a new release，tag 填 `v2.1.0`；
2. 上传 `TenStat-win-x64.zip`（`publish.ps1 -Zip` 生成，约 34 MB）；
3. 说明里写清楚：解压后双击 `TenStat.exe`，不需要安装、不需要管理员权限、不联网；
4. 把 `docs/images/` 里的截图贴进 Release 说明。

### 4.4 仓库设置建议

| 项 | 建议 |
| --- | --- |
| Description | `Windows TenStat：鼠标/键盘次数与移动距离，全部本地 SQLite，含透明 Live2D 桌宠` |
| Topics | `winui3` `windows-app-sdk` `csharp` `sqlite` `live2d` `keyboard-hook` `mouse-hook` `privacy` `offline` `desktop-pet` |
| License | MIT（仓库根目录的 `LICENSE` 记得把 `<你的名字>` 换成你的名字/ID） |
| Releases | 只放 `TenStat-win-x64.zip`，不要放 `dist/` 整个目录 |
| 安全提醒 | 在 README 的 FAQ 里已经写明「全局钩子可能触发杀软提示」，Release 说明里再提一句更稳 |

### 4.5 截图与模型

- `docs/images/*.png` 是界面截图，可以提交（总共约 1 MB）；
- **Live2D 模型（`xiaoyue*` 目录、`.moc3`、8192² 贴图）不要提交**：版权归原作者，体积也大。
  `.gitignore` 里已经忽略 `xiaoyue0825_*/`；程序是运行时读本地模型目录的，仓库里不需要它；
- 别人 clone 后想用桌宠：自己准备一个 `.model3.json` + `.moc3` + 贴图，在 设置 → 桌宠 → 选择目录 指过去即可。
