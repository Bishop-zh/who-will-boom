# UGUI 视觉重构前审计（第一阶段）

记录日期：2026-09-20

## 范围与约束

- 第一阶段仅覆盖：主大厅/建房加入界面、联机等待房间、游戏 HUD、游戏内设置。
- 不修改玩法、网络协议、棋盘生成、AI 决策、回合推进与存档数据结构。
- 结算界面本阶段只保证现有入口与引用不受影响，不做布局重构。

## 当前 UI 构建方式

项目没有大厅、HUD、设置对应的序列化 UGUI Prefab。界面由运行时代码创建，原有“Prefab 引用”实际由以下控制器与组件工厂承担：

- `PreGameMenuUgui`：主大厅、身份选择、单人/开房/加入、主菜单设置。
- `LobbyUI`：联机等待房间、地址复制、客户端重连、座位与开始游戏。
- `GameHudUgui`：回合条、玩家信息、事件流、道具栏、概率提示、横幅与结算入口。
- `InGameSettingsUgui`：Esc 设置遮罩、显示/音频/游戏/辅助设置及退出入口。
- `UiKit`：Canvas、SafeArea、Panel、Button、Input、Label 及布局组的运行时工厂。
- `UiTheme`：颜色、字号、间距及程序化九宫格图片。

## Canvas 与层级快照

### PreGameMenuCanvas（sortingOrder 220）

- SafeArea
  - Brand
  - RosterPanel
  - SessionPanel
  - Footer
  - SettingsBlocker
  - SettingsPanel

### LobbyCanvas（sortingOrder 230）

- SafeArea
  - LobbyPanel
    - 标题与房间地址
    - LobbyActions
    - ClientControls
    - 四个 Seat 卡片
    - 状态/通知
    - 开始游戏

### HudCanvas（sortingOrder 100）

- SafeArea
  - TopBar / TurnStatus
  - PlayerPanel
  - EventFeed
  - InventoryHotbar
  - HoverTooltip
  - SelectedItemCard
  - Banner / Pulse
  - GameOver（本阶段不重构）
  - SettingsBlocker / InGameSettingsPanel

## 按钮与输入绑定快照

当前没有 Inspector 中的持久化 `Button.onClick`；全部由代码 `AddListener` 绑定。重构必须保留下列方法引用：

- 主大厅：`ToggleSettings`、`ToggleVolume`、`ToggleFullscreen`、`ToggleVsync`、`ToggleVoiceTest`、`ToggleVoiceMode`、`CycleMap`、`CycleBombs`、`CycleDrift`、`CycleTurnTime`、`ToggleRules`、`SelectDiscoveredRoom`、`StartGame`。
- 身份和模式按钮：闭包更新 `SelectedSkinIndex` / `ModeIndex`；昵称、地址、端口输入分别写回 `PlayerName` / `Address` / `PortText`。
- 等待房间：`CopyHostAddress`、`SessionContext.ReturnToMainMenu`、`RetryConnect`、`lan.StartGame`。
- 游戏内设置：`Close`、`CycleMode`、`CycleResolution`、`CycleQuality`、`ToggleVsync`、`ToggleVoiceTest`、`ToggleVoiceMode`、`ToggleRules`、`ExitToMenu`、`Application.Quit`。
- HUD：设置入口、1~6 道具选择、结算页原有 `OnRestart` / `OnBackToLobby` / `OnQuitGame` 均必须保留。

## 字体与适配基线

- 当前 `UiKit` 使用旧版 `UnityEngine.UI.Text` 和系统动态字体。
- TextMeshPro 包版本为 3.0.7；开始重构时工程尚未导入 TMP Essential Resources，也没有中文 TMP Font Asset。
- 当前桌面 Canvas 会根据实际窗口在 1600×900、1920×1080、2560×1440 三套参考分辨率间切换；本次目标改为桌面统一 2560×1440、Match 0.5，并依靠 Anchor/SafeArea 与响应式布局适配三种测试分辨率。

## 不变引用

- 场景中的 `GameDirector`、`LanNetworkManager`、`PreGameMenu` 和现有运行时数据源保持不变。
- UI 继续读取原事件/状态，不新建第二套游戏事件系统。
- 棋盘、角色、桌子、3D 酒馆场景与摄像机不在本次修改范围。
