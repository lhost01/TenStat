# 架构与实现说明

## 1. 单进程 + 透明桌宠浮层

```
TenStat.exe（唯一进程）
├─ 主窗口：WinUI 3 / Fluent 2 / Mica，NavigationView + Frame
├─ 桌宠浮层：一个无边框透明窗口（同进程），内部是 WebView2 + Live2D
├─ 全局低级钩子（WH_MOUSE_LL / WH_KEYBOARD_LL）
├─ SQLite（%LocalAppData%\TenStat\stats.db）
└─ 托盘 + 全局热键（自己实现的 message-only 窗口）
```

桌宠为什么不是主窗口里的 XAML 元素：

| 方案 | 透明 | 结论 |
| --- | --- | --- |
| `Microsoft.UI.Xaml.Controls.WebView2` 内嵌 | ✗ windowed hosting，背景永远是一整块白/黑 | 否决 |
| 独立 exe（早期 WPF 版） | ✓ | 否决（用户不要第二个进程） |
| **同进程无边框窗口 + DirectComposition + composition WebView2** | ✓ | **采用** |

具体做法（`Controls/PetOverlay.cs`）：

1. `RegisterClassEx` + `CreateWindowEx(WS_POPUP | WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP)`，无重定向位图才能让 DComp 全权负责绘制；
2. `new Windows.UI.Composition.Compositor()` → 通过 `ICompositorDesktopInterop.CreateDesktopWindowTarget(hwnd, true)`（自己声明的 COM 接口）
   拿到 `DesktopWindowTarget`，挂一个 `ContainerVisual` 当根；
3. `environment.CreateCoreWebView2CompositionControllerAsync(...)`，`RootVisualTarget = 那个 ContainerVisual`，
   再 `DefaultBackgroundColor = Colors.Transparent` —— 只有 composition 模式这条路径下透明才真的生效；
4. DPI：窗口尺寸按 `GetDpiForWindow` 换算成物理像素，controller 设 `RasterizationScale` + `Bounds`；
5. 交互全在宿主：`WM_LBUTTONDOWN/MOUSEMOVE/LBUTTONUP` 自己做拖动与"点击判定"，`WM_MOUSEWHEEL` 缩放；
   页面不需要收鼠标，只负责渲染与显示气泡。

> 踩坑记录：
> 1. `typeof(Compositor)` 强转 `ICompositorDesktopInterop` 会 `InvalidCastException`（CsWinRT 对象不是 RCW），
>    要 `MarshalInspectable<Compositor>.FromManaged` 拿 ABI 指针再 `Marshal.GetObjectForIUnknown` 做 QI；
> 2. `DesktopWindowTarget` 的命名空间是 `Windows.UI.Composition.Desktop`（不是 `Windows.UI.Composition`）；
> 3. `CreateCoreWebView2CompositionControllerAsync` 要 `CoreWebView2ControllerWindowReference`，不是 `IntPtr`；
> 4. `WebResourceRequested` 对虚拟主机映射的地址不触发（实测零回调），模型配置改成 `AddScriptToExecuteOnDocumentCreatedAsync` 注入；
> 5. `pixi-live2d-display@0.5.0` 不存在（只有 0.5.0-beta），用 0.4.0 + pixi 6.5.10。

页面文件在 `Assets\Live2D\`，虚拟主机映射：

| 虚拟主机 | 指向 | 用途 |
| --- | --- | --- |
| `app.local` | `程序目录\Assets\Live2D` | 页面与渲染库 |
| `model.local` | 用户选择的模型目录（只读） | `.model3.json` / `.moc3` / 贴图 / motion3 / exp3 |

视线跟随（`PetOverlay.UpdateFocus` + `pet.js` 的 `focus` 分支）：

1. 宿主每 150ms 取一次全局光标，换算成页面 CSS 像素坐标（`(cursor - windowRect) / rasterizationScale`）发给页面；
2. 页面换算成 -1..1 的方向，做 0.18 的平滑插值；
3. 两层效果：① 整只模型朝指针方向平移（±18px / ±10px，肉眼可见）；② 每帧在 `beforeModelUpdate`
   里写 `ParamAngleX/Y/Z`、`ParamEyeBallX/Y`、`ParamBodyAngleX`（此时动作/眨眼/物理都算完、`coreModel.update()` 之前）。
   本机这个模型对 `ParamAngleX` 不响应（有范围但被动作管线覆盖），所以平移是主要可见效果；
   换一个绑定了头转参数的模型，头/眼会一起转。

「只在本程序内显示」（`SettingsService.PetStayInsideApp`，默认开）：

- 每次 tick 看 `GetForegroundWindow()`：等于主窗口（或桌宠窗口自己，它是 `WS_EX_NOACTIVATE`，点它不抢前台）
  且主窗口可见才显示，否则 `ShowWindow(SW_HIDE)`；
- 拖动时把目标坐标夹在主窗口矩形内（允许露出 40px，别卡死）；
- 关掉开关 = 常驻置顶，可拖到屏幕任意位置。
说话与互动：`PetDialogue` 按今日统计（左键、右键、移动米数、按键总数、Top 键、活跃时长、时段）生成台词，
宿主用 `{cmd:'say', text}` 发给页面，页面弹气泡并顺手换个表情；单击/每 75 秒（暂停时不打扰）各触发一次。
## 2. 主程序分层

```
┌──────────────────────────────────────────────────────────────┐
│ Views（XAML）  Dashboard / Mouse / Keyboard / History          │
│                Settings / About + MainWindow（NavigationView） │
└───────────────▲──────────────────────────────────────────────┘
                │ x:Bind（OneWay / TwoWay）
┌───────────────┴──────────────────────────────────────────────┐
│ ViewModels（CommunityToolkit.Mvvm）                            │
│   PageViewModelBase：页面可见时 600~2000ms 轮询一次快照         │
│   ShellViewModel：状态徽章 / 暂停 / 倒计时 / 导航请求            │
└───────────────▲──────────────────────────────────────────────┘
                │ 只读内存快照 + 少量 DB 查询（历史页）
┌───────────────┴──────────────────────────────────────────────┐
│ Services                                                      │
│   MouseHookService / KeyboardHookService  ← 只做轻量计数        │
│   StatsService        ← 汇总、暂停、排除、活跃时长、落盘、趋势    │
│   StorageService      ← SQLite（单连接 + 锁 + WAL）             │
│   SettingsService     ← AppSettings 强类型包装                  │
│   ExclusionService    ← 前台进程名匹配                          │
│   TrayIcon            ← Shell_NotifyIcon + RegisterHotKey      │
│   ExportService / PetService / StartupService / AppLog         │
└──────────────────────────────────────────────────────────────┘
```

`AppServices` 是一个极简服务定位器（静态单例），不引 DI 容器：这个程序的依赖图只有 9 个对象，容器带来的间接层不划算。

## 3. 线程模型（关键）

```
输入 → [OS 低级钩子回调，运行在 UI 线程的消息循环上]
          │  只做：Interlocked.Increment / 一次 sqrt / 字典累加
          ▼
      hook 内部缓冲（MouseHookService / KeyboardHookService）
          ▲ 每 300ms Drain() 一次（DispatcherQueueTimer）
          │
   StatsService.TickAsync()  ← UI 线程
          │  ① 跨天检查 ② 暂停到期 ③ 排除应用检查
          │  ④ 合并增量到"今日/会话/未落盘" ⑤ 活跃时长 ⑥ 必要时落盘
          ▼
      Updated 事件 → 各页面 VM 取快照 → x:Bind 刷新
          │
   FlushAsync() → Task.Run → SQLite 事务（不占 UI 线程）
```

三条纪律：

1. **钩子回调只做加法**。不碰数据库、不碰 UI、不分配大对象；整个 body 包在 try/catch 里，异常绝不穿过 P/Invoke 边界。
2. **UI 固定节奏拉取**，不让钩子推 UI。页面不可见时 `Detach()` 停表，零开销。
3. **落盘在后台线程**，但"总计基线"先在内存里加上增量，这样 UI 上的总数不会因为落盘而回跳；写失败会把增量回滚回内存缓冲，下次重试。

## 4. 统计口径怎么算

| 口径 | 数据来源 |
| --- | --- |
| 今日 | 启动时从 `DailyStat` / `KeyStat` 读当天值作为基线，加上内存增量 |
| 本次会话 | 进程内计数器（`_session*`），每 15 秒同步进 `SessionStat` 表 |
| 历史总计 | `SUM(DailyStat)` 基线 + 未落盘增量 |

三种口径互不影响：切换只改展示，后台一直在往"今日"累加。

## 5. 落盘策略

- 触发条件：未落盘事件数 ≥ 100 **或** 距上次落盘 ≥ 设置里的「自动保存间隔」（默认 15 秒）；
- 落盘内容：`DailyStat` 一行累加 + 每个按过的键各一条 UPSERT + `SessionStat` 更新；
- 关闭/退出/跨天前都会 `FlushAsync()`；
- 启动时 `PRAGMA wal_checkpoint(TRUNCATE)` + `PRAGMA quick_check`，坏了就把旧库改名 `stats.db.corrupt-<时间戳>` 留档重建。

## 6. 暂停与排除

- **手动暂停**：`IsPaused`；`PauseFor(TimeSpan)` 带到期时间，tick 里判断到期自动恢复，UI 显示倒计时。
- **排除应用**：`ExclusionService` 每次 tick 用 `GetForegroundWindow` + `GetWindowThreadProcessId` 取进程名，命中用户列表 → `IsExcluded`（红点「已暂停」，副标题写明命中的进程名）。
- **不排除自己**：按需求，前台是本程序时也照常统计（点自己的界面、按快捷键都算数），所以 `IsForegroundAlwaysExcluded` 恒为 false，`IsSuppressedBySelf` 只作保留字段。
- 暂停/排除期间钩子照常回收增量，但 `StatsService` 直接丢弃，不写内存、不写库。

## 7. 按键统计的隐私边界

- 只用 `KBDLLHOOKSTRUCT.vkCode`（虚拟键码）；
- **不调用** `GetKeyboardState` / `MapVirtualKey` 取字符 / `ToUnicode` / 输入法查询；
- 不区分大小写与 Shift 组合，`A` 与 `a` 都是 `VK 0x41`；
- 未知键显示 `VK_6A` 这种形式；
- 长按重复默认过滤：用「按下集合 + KEYUP 移除」判断，比时间窗可靠。

## 8. 鼠标移动距离

```csharp
var dx = pt.X - _lastPoint.X;
var dy = pt.Y - _lastPoint.Y;
var distance = Math.Sqrt(dx * dx + dy * dy);
if (distance > 0 && distance < 2000) _pixels += distance;   // ≥2000 视为跳变（切显示器 / 远程桌面 / 加速）
else if (distance >= 2000) DroppedJumps++;                  // 计数在 UI 上可见，便于排查
```

米数换算：`meters = pixels / (96 × scale) × 0.0254 × calibration`，`scale` 取自 `GetDpiForWindow`，`calibration` 是设置页的手动校准系数（默认 1.0）。

## 9. 趋势与 Top N

- 最近 1 小时：内存字典按「分钟」聚合点击/按键，缺的分钟补 0，画 60 点折线（`MiniLineChart` 自绘）。
- 键盘 Top 10 / Top 5：当前口径的按键排行，进度条按「相对最高频次」缩放，右侧显示占比与最后按下时间。
- 历史趋势：`GetRecentDaily(n)` 按日期聚合点击总数，画面积折线。

## 10. 自绘控件（不引图表库）

- `Controls/MiniLineChart.cs`：`Values`（`IReadOnlyList<double>` 以 `object` 形式挂 DP，绕开 WinRT 的泛型限制）、网格线、面积、折线、末点强调；重绘抛到 `DispatcherQueue` 下一拍，避免在绑定回调里改视觉树。
- `Controls/DonutRing.cs`：`ArcSegment` 画的环形比例，`Value` 取 0~1。

踩到的坑：WinUI 的 `PointCollection` 不能同时挂到两个元素上（`PolyLineSegment` 与 `Polyline` 共用会抛 `E_INVALIDARG`），所以面积和折线各建一份。

## 11. 托盘与热键

自己写（不引 H.NotifyIcon）：一个 `HWND_MESSAGE` 隐藏窗口收 `WM_TRAY_CALLBACK`、`WM_HOTKEY` 与菜单命令；`TrackPopupMenu` 前先 `SetForegroundWindow`，否则点外面菜单不消失；退出时 `NIM_DELETE` + `DestroyWindow` + `DestroyIcon` 全套释放。热键注册失败（例如被别的程序占用，Win32 1409）只降级为黄色提示，不影响统计。
