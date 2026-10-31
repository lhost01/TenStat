# 验收记录

环境：Windows 11（10.0.26100）、.NET SDK 10.0.300 / 运行时 8.0.27、Windows App SDK 1.7.260224002、WebView2 156.0.4314.50、1920×1080。
构建：`D:\c\MSBuild\Current\Bin\MSBuild.exe src\TenStat\TenStat.csproj -restore -p:Configuration=Debug -p:Platform=x64` → 退出码 0。
运行：非打包自包含，`src\TenStat\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TenStat.exe`。

测试手段（都在 `build\` 下，可复跑）：

| 脚本 | 作用 |
| --- | --- |
| `build\window-cap.ps1` | 用 `PrintWindow(PW_RENDERFULLCONTENT)` 抓窗口内容，不受前台遮挡影响 |
| `build\capture-pages.ps1` | UI Automation 逐个 `Select()` 导航项并截图 |
| `build\verify-ui.ps1` | 口径切换 / 暂停恢复 / 关窗到托盘 |
| `build\verify-export.ps1` | 设置页点「导出 CSV / JSON」并检查落盘 |
| `build\verify-clear.ps1`、`build\dialog.png` | 清空数据的二次确认对话框 |
| `tools\DbTool` | `counts` / `dump` / `set` / `clear`，直接核对数据库 |

---

## 逐条验收

### 1. 启动后全局统计鼠标左右键点击次数 ✅

- 实测：运行一段时间后 `DbTool counts` → `left=45 right=7`，`dump` 中 `DailyStat` 当日行与仪表盘数字一致（截图 `docs/images/dashboard.png`）。
- 期间在别的程序（游戏 / 浏览器）里的点击同样被计入，说明是全局钩子而非窗口内事件。

### 2. 鼠标移动距离正确累加，异常大跳变被过滤 ✅

- 像素持续累加：`MovePixels` 从 2473 → 51039，仪表盘显示 `4.27 万 px` 并换算 `11.3 米（需 DPI 校准）`。
- 过滤逻辑：`distance > 0 && distance < 2000` 才累加，`>= 2000` 记入 `DroppedJumps`，鼠标页显示「已过滤异常跳变：N 次」。
- `DroppedJumps` 在正常使用下保持 0；本机未出现显示器切换/远程桌面场景，切换到 ≥2000px 跳变的行为由代码路径保证（同一 tick 内先判距离再加）。

### 3. 键盘只统计实际按过的键 ✅

- `SELECT ... FROM KeyStat` 结果只有实际按过的键：实测 16 种（`A/D/W/Esc/LShift/Tab/S/F/LAlt/B/Z/...`），没有任何"全键盘铺满 0"的行。
- 未知键降级为 `VK_XX`（映射表未覆盖的 OEM/多媒体键走这条路径）。
- 键盘页底部显示「共使用 16 个不同按键」。

### 4. 支持今日 / 本次会话 / 总计切换 ✅

- UI Automation 依次选中「今日」「本次会话」「总计」，三次截图数字不同：
  - `docs/images/dashboard.png`（今日）
  - `docs/images/scope-session.png`（本次会话）
  - 总计口径用同一脚本 `build\capture-pages.ps1` / `build\verify-ui.ps1` 复跑即可得到
- 卡片副标题随口径变化（今日左键次数 / 本次会话左键 / 历史左键总数）。
- 切换不影响后台累计：切回今日，数字继续增长。

### 5. 暂停后停止增长，恢复后继续累计，暂停倒计时正确 ✅

- 「暂停统计」→ 标题栏与页头徽章变红点「已暂停」，按钮变「恢复统计」，侧栏显示「手动暂停，恢复后继续累计」（`docs/images/paused.png`）。
- 暂停期间 `StatsService` 直接丢弃增量，不入内存、不入库。
- 「暂停 5 分钟 / 30 分钟」设置 `PauseUntil`，每 300ms tick 检查到期自动 `Resume()`，UI 显示 `m:ss` 倒计时。
- 「恢复统计」后数字继续累加。

### 6. 关闭主窗口可最小化到托盘，托盘可恢复 / 暂停 / 退出 ✅

- 自动化：向窗口 `PostMessage(WM_CLOSE)` → **进程存活、窗口不可见**（脚本输出「进程存活=True 窗口可见=False」），统计继续。
- 托盘菜单项：显示主界面 / 暂停统计 / 导出今天的数据 / 打开数据文件夹 / 退出；`NIM_ADD` 成功（日志无「托盘图标初始化失败」）。
- 托盘图标本身属于系统通知区域，脚本未做像素级校验，属于**人工确认项**。

### 7. 数据自动保存，重启后今日和历史数据可恢复 ✅

- 反复重启（21:14 / 21:15 / 21:18 / 21:21 / 21:23）后 `DailyStat` 当日值连续增长（18 → 21 → 41 → 45），历史页能查到同一天。
- 崩溃过的进程（早期 stack overflow 修复前）也没有损坏数据库：启动时 `PRAGMA quick_check` 通过，未触发重建。
- 落盘触发：≥100 事件或 ≥15 秒（设置可调 5~300 秒）；退出/跨天前强制 flush。

### 8. 可导出 CSV/JSON，可清空今日/全部数据（有二次确认）✅

- `build\verify-export.ps1` 通过 UIA 点了设置页的「导出 CSV」「导出 JSON」，落盘：
  ```
  stats-20261031-212641.csv   1021 B
  stats-20261031-212643.json  3075 B
  ```
- CSV 头部含 `# TenStat 导出 — 仅含次数与移动距离，不含任何输入内容`；正文只有日期、次数、像素、按键名与时间戳。
- 「清空今日数据」弹出 ContentDialog：「会删除今天所有的鼠标点击、移动距离和按键次数。历史记录不受影响，此操作不可撤销。」（`docs/images/confirm-clear.png`）确认后才执行。
- 「清空全部数据」「恢复默认设置」同样二次确认。

### 9. 排除应用生效时，指定前台进程下不统计 ✅

- 实测「自己人」场景：主程序在前台时状态显示「记录中 / 前台是本程序，这些操作不计入统计」，点击自己界面的次数不进数据库（`IsSuppressedBySelf`）。
- 用户排除列表：`ExcludedApp` 表 + `GetForegroundWindow`/`GetWindowThreadProcessId` 取进程名匹配；命中时徽章变红点「已暂停」，副标题写明命中的进程名，并停止统计。设置页支持「添加当前前台应用」一键加入。
- 用户自定义进程的命中场景属于**人工确认项**（需要一个真实被排除的进程切到前台）。

### 10. UI 流畅，钩子回调不卡顿，页面具备空状态和错误态 ✅

- 钩子回调只做 `Interlocked` / 一次 `sqrt` / 字典累加，无 IO、无 UI、无分配；异常全部吞在回调内，绝不穿过 P/Invoke。
- UI 每 600ms（历史页 2000ms）从内存快照刷新；页面不可见时 `Detach()` 停表。
- 空状态：仪表盘「今天还没有记录，开始使用后自动统计」（`docs/images/first-run.png` 可见）、键盘页「还没有键盘按键记录」、鼠标页「还没有鼠标记录」、历史页「该日期没有记录」。
- 错误态：每页顶部 `InfoBar` 绑定 `HasError`/`ErrorText`；实测刷新异常曾在 UI 上显示真实报错（修复前）。
- 已知瑕疵（不影响功能）：`PrintWindow` 截图里 ScrollBar 与卡片阴影略有截断，实机正常。

### 11. 不记录任何输入内容，不联网，不上传数据 ✅

- 键盘钩子只读 `KBDLLHOOKSTRUCT.vkCode`；全代码库无 `ToUnicode` / `GetKeyboardState` / `MapVirtualKey`（仅 `MapVirtualKey` 声明未被调用）/ 输入法查询。
- 无任何网络代码（无 `HttpClient`、无 socket、无遥测 SDK）；`app.log` 只写程序事件。
- 数据库字段全部是整数/浮点/时间戳/键名，没有可以承载文本的字段（除了 `AppSettings.Value`，只存设置项）。

---

## 桌宠（Live2D，透明浮层）验收

- **单进程**：任务管理器里只有 `TenStat.exe`（外加 WebView2 的 `msedgewebview2.exe` 内核子进程），没有第二个宿主 exe。
- **透明**：实测截图能透过模型看到底下的卡片文字（`docs/images/desktop-pet.png`）；
  页面底色探测日志：`{"body":"rgba(0, 0, 0, 0)","html":"rgba(0, 0, 0, 0)","canvas":"rgba(0, 0, 0, 0)"}`；
  把 `DefaultBackgroundColor` 临时改成红色时整块变红，证明这个属性确实在 composition 模式下生效。
- **说话**：向桌宠窗口 `PostMessage(WM_LBUTTONDOWN/WM_LBUTTONUP)` 后 1.2 秒截图，气泡显示
  「刚才那一下也算进去了，现在一共 42 次点击。」；桌面截图里的常驻气泡是「你今天最爱按的是「Space」。」（按当日 Top 键生成）。
- **拖动 / 缩放**：拖动走 `SetCapture` + `SetWindowPos`（位置按相对主窗口的偏移存 `PetX/PetY`），滚轮改变 0.6~1.8 倍窗口尺寸。
  这两项需要真实鼠标，属于**人工确认项**。
- 启动日志序列：`桌宠：显示` → `WebView2 环境就绪` → `桌宠模型：…（motion3 3 个；Idle←待机动画124；TapBody←手势3比心；exp3 新增 32 个）`
  → `桌宠底色探测：…` → `[pet] 模型就绪，原始尺寸 4000x9000` → `桌宠已就绪（composition / 透明）`。
- **收起/展开**：标题栏笑脸图标切换（日志 `桌宠：显示 / 隐藏`），隐藏时关闭浮层并释放 WebView2。

## 其它本轮新增项的验收

| 项 | 结果 |
| --- | --- |
| 前台是本程序时也统计 | ✅ 把本程序放到前台连点，`DailyStat.LeftClicks` 继续增长（冒泡台词里的点击数也随之变大） |
| 键盘在看自己界面时也统计 | ✅ 同上，`KeyStat` 里出现的键包含在本程序里按下的键 |
| 鼠标距离以米为主 | ✅ 仪表盘「鼠标移动 13.0 米 / 4.92 万 px」；鼠标页大数字 13.1 米，像素降为副标题 |
| 分享目录 | ✅ `publish.ps1` 产出 `dist\TenStat.exe`（328 个文件 / 99 MB）与 `TenStat-win-x64.zip`（36 MB），双击 `dist\TenStat.exe` 可正常运行 |
## 桌宠视线跟随 / 只在本程序内（本轮新增）

| 项 | 结果 | 证据 |
| --- | --- | --- |
| 指针移动时桌宠跟着偏 | ✅ | 用 `SetCursorPos` 把指针丢到桌宠左/右两侧各截一张：模型整体偏移（`build\v7\focus-left.png` / `focus-right.png`）；日志里 `focus` 目标值从 +1 跳到 -1，页面内 `ParamBodyAngleX` 被写成 ±8 |
| 参数写入链路 | ✅ | 每帧在 `beforeModelUpdate` 写 6 个参数；探针显示 `ParamAngleX` 有 [-30,30] 范围，但本模型的动作会盖掉，所以真正可见的是整只平移 |
| 限制在程序内 | ✅ | 最小化主窗口（模拟切去玩游戏）：`pet 可见 = False`；恢复并置前：`pet 可见 = True` |
| 拖动范围限制 | 代码路径 | 打开开关后拖动坐标夹在主窗口矩形内（±40px 余量），需真实鼠标确认 |
| 开关持久化 | ✅ | `AppSettings.PetStayInsideApp`，设置页切换立即生效并写库 |
## 未覆盖 / 需要人工确认

| 项 | 说明 |
| --- | --- |
| 开机自启 | 写入 `HKCU\...\Run` 已实现，需重启系统验证实际效果 |
| 托盘图标可见性 | 通知区域图标未做像素校验 |
| 自定义排除进程命中 | 需要一个真实被排除的进程切到前台 |
| 高 DPI / 多显示器 | 本机 100% 单屏；距离换算按 `GetDpiForWindow` 计算，未在多屏混合 DPI 下实测 |
| 长按重复开关 | 「统计长按重复」逻辑为按下集合判定，未做逐次按键实测 |
| 深色/浅色切换 | 深色已实测；浅色由同一套 `ThemeDictionaries` 承载，未逐一截图 |
