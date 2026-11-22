# ww-tasker · 鸣潮地区任务查漏补缺

一个贴在《鸣潮》画面上的小清单。左边是按地区分的任务表，右边一条一条打勾，**不读内存、不注入、不截屏**——就是一张你自己点的表。

![主界面](docs/shot-main.png)

## 它是干什么的

清地图最烦的不是打怪，是「这个地区我到底还差哪个任务」。这软件就是干这个的：把每个地区要做的任务列成一张表，你做到哪就勾到哪，剩下的自然浮出来。

- **只要任务**。主线、幕间、同行、支线、探索，宝箱声匣成就一概不管。
- **三态**：未开始 / 进行中 / 已完成。点圆圈就换一档，不用开下拉框。
- **隐藏任务单独标出来**。那种没有追踪、得靠走近才触发的，用灰色 ▸ 标记，免得你满地图乱转找一个根本不会出现在任务列表里的东西。
- **每条能写备注**。右键写一行，比如「前置没做完」。

## 怎么用

双击 `WuwaChecklist.exe`。就这一个文件，没有安装程序，不需要装 .NET。

| 按键 | 干什么 |
| --- | --- |
| `Ctrl+Alt+W` | 显示 / 隐藏 |
| `Ctrl+Alt+K` | 锁定（点击穿透，鼠标穿过面板点游戏）|
| `Ctrl+Alt+F` | 折叠成一条细条 |

三个键都能在 `⋯` → `快捷键…` 里改。

穿透那个特别好用：勾完就锁上，面板还挂在那儿，但鼠标可以直接点底下的游戏，不会误触。

## 关于「游戏里看不见」

**独占全屏模式下，Windows 上任何悬浮窗都画不出来**，这不是这个程序的问题，是系统的物理限制（全屏那一层独占显示输出）。想要它盖在游戏上，把游戏设成**无边框窗口 / 窗口化全屏**。

程序检测到前台是全屏应用时会在面板底下挂一行提示：`独占全屏时看不见我，Ctrl+Alt+W 试一下`。

## 任务库

任务列表放在 [`data/quests.json`](data/quests.json)，跟程序分开。改数据不用重新编译。

程序内 `⋯` → `检查任务库更新…` 会从仓库拉最新的那份，拉失败就继续用本地/内置的，不会把程序搞坏。

格式长这样：

```json
{
  "schemaVersion": 1,
  "gameVersion": "3.0",
  "regions": [
    {
      "id": "huanglong.jinzhou",
      "name": "今州",
      "chapter": "1.0 瑝珑",
      "order": 10,
      "quests": [
        { "id": "jz.main.01", "name": "第一幕：嘤鸣初相召", "type": "main", "subArea": "今州城" },
        { "id": "jz.expl.01", "name": "雨夜的旅人", "type": "expl", "subArea": "今州城",
          "hidden": true, "note": "先做「追月节」才刷" }
      ]
    }
  ]
}
```

| 字段 | 说明 |
| --- | --- |
| `type` | `main` 主线 / `tale` 幕间 / `comp` 同行 / `side` 支线 / `expl` 探索 |
| `hidden` | `true` 表示无追踪、要触发的隐藏任务 |
| `subArea` | 子地图名，显示在任务名下面 |
| `note` | 写死在库里的说明（你自己写的备注在存档里，不在这） |

> ⚠️ **`id` 定下来就不要改了。** 存档只记 id 不记任务名，改 id 等于那条记录丢了。改名字、改错字、改子地图都不影响进度。

## 存档

存在 `%APPDATA%\ww-tasker\profile.json`，支持多个存档（`⋯` → 存档名 → 新建/改名/删除）。

覆盖安装、换新版 exe 都不影响，因为这个文件在 AppData 里，不在程序目录。

## 自己编译

需要 .NET SDK（8 或更高）。目标框架是 .NET Framework 4.8，产出一个单文件 exe，Windows 10/11 自带运行时，别人拿去双击就能跑。

```powershell
dotnet build src/WuwaChecklist.csproj -c Release
# 产物：src/bin/Release/WuwaChecklist.exe
```

界面全用 C# 手写（没有 XAML），配色和尺寸在 `src/Theme.cs`，绘制在 `src/PanelView.cs`。

## 发版

打 tag 就自动打包：

```bash
git tag v0.1.0
git push origin v0.1.0
```

`.github/workflows/release.yml` 会编译、打 zip、用 [git-cliff](https://github.com/orhun/git-cliff) 按 `cliff.toml` 生成更新日志，然后发到 Releases。

提交信息按 [Conventional Commits](https://www.conventionalcommits.org/) 写（`feat:` / `fix:` / `data:` …），更新日志才分得清组。

## 边界

- 不读游戏内存，不注入进程，不截屏识别，不上传任何数据。
- 联网只有两件事：点「检查任务库更新」拉一次 JSON，以及启动十几秒后问一次有没有新版。都是 GET，失败了就当没发生。

## 授权

MIT，见 [LICENSE](LICENSE)。
