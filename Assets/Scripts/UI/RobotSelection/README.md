# 兵种选择 UI 系统

## 概述

这是一个优雅的弹窗式兵种选择系统，在客户端启动时自动显示，用于选择红蓝方阵营和兵种。

## 架构设计

采用 MVVM 模式：

```
┌─────────────────────────────────────────────────────────────────┐
│                        RobotSelectionBootstrap                  │
│                     (启动入口，挂载到场景)                         │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      RobotSelectionPanel (View)                 │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  - 动态创建 Canvas (sortingOrder=9999，最高层级)             │  │
│  │  - 阵营选择按钮 (红方/蓝方)                                  │  │
│  │  - 兵种选择网格 (3x3 布局)                                   │  │
│  │  - 确认按钮 + 状态提示                                       │  │
│  └───────────────────────────────────────────────────────────┘  │
│                              │                                  │
│                              ▼                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │            RobotSelectionViewModel (ViewModel)            │  │
│  │  - SelectedTeam: 当前阵营                                  │  │
│  │  - SelectedRobot: 当前兵种                                 │  │
│  │  - CanConfirm: 是否可确认                                   │  │
│  │  - PropertyChanged: 属性变更通知                            │  │
│  │  - SelectionCompleted: 选择完成事件                         │  │
│  └───────────────────────────────────────────────────────────┘  │
│                              │                                  │
│                              ▼                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │              RobotSelectionData (Model)                   │  │
│  │  - TeamColor: 红方/蓝方                                     │  │
│  │  - RobotType: 兵种枚举                                      │  │
│  │  - RobotSelectionResult: 选择结果                           │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

## 文件清单

| 文件                         | 用途                              |
| ---------------------------- | --------------------------------- |
| `RobotSelectionData.cs`      | 数据模型：阵营/兵种枚举，选择结果 |
| `RobotSelectionViewModel.cs` | 视图模型：状态管理，业务逻辑      |
| `RobotSelectionPanel.cs`     | 视图：UI 构建，渲染，交互         |
| `RobotSelectionBootstrap.cs` | 启动器：自动显示，全局访问        |
| `UIManager.cs`               | UI 管理器：弹窗生命周期管理       |

## 使用方式

### 方式一：自动启动（推荐）

1. 在 MainScene 中创建一个空 GameObject
2. 挂载 `RobotSelectionBootstrap` 组件
3. 运行时自动弹出选择界面

```csharp
// 在其他脚本中获取选择结果
if (RobotSelectionBootstrap.IsSelectionCompleted)
{
    var result = RobotSelectionBootstrap.CurrentSelection;
    Debug.Log($"阵营: {result.Team}, 兵种: {result.Robot}, ID: {result.RobotId}");
}

// 或订阅事件
RobotSelectionBootstrap.OnSelectionCompleted += (result) =>
{
    Debug.Log($"选择完成: {result}");
};
```

### 方式二：代码调用

```csharp
// 显示选择面板
RobotSelectionPanel.Show(result =>
{
    Debug.Log($"用户选择了: {result}");
    // 在这里处理选择结果
});

// 强制隐藏面板
RobotSelectionPanel.Hide();
```

### 方式三：与 NetworkManager 集成

```csharp
// 在 NetworkManager 或其他服务中等待选择完成
private void Start()
{
    if (!RobotSelectionBootstrap.IsSelectionCompleted)
    {
        RobotSelectionBootstrap.OnSelectionCompleted += OnRobotSelected;
    }
    else
    {
        OnRobotSelected(RobotSelectionBootstrap.CurrentSelection);
    }
}

private void OnRobotSelected(RobotSelectionResult result)
{
    // 使用 result.RobotId 进行网络通信
    SendRobotIdToServer(result.RobotId);
}
```

## Inspector 配置

`RobotSelectionBootstrap` 组件提供以下配置：

| 属性              | 说明                                |
| ----------------- | ----------------------------------- |
| `autoShowOnStart` | 是否在启动时自动显示 (默认: true)   |
| `skipSelection`   | 跳过选择界面直接使用默认值 (调试用) |
| `debugTeam`       | 调试模式下的默认阵营                |
| `debugRobot`      | 调试模式下的默认兵种                |

## 兵种 ID 映射

| 阵营 | 兵种       | RobotId |
| ---- | ---------- | ------- |
| 红方 | 英雄       | 1       |
| 红方 | 工程       | 2       |
| 红方 | 3号步兵    | 3       |
| 红方 | 4号步兵    | 4       |
| 红方 | 5号步兵    | 5       |
| 红方 | 空中机器人 | 6       |
| 红方 | 哨兵       | 7       |
| 红方 | 飞镖       | 8       |
| 红方 | 雷达站     | 9       |
| 蓝方 | 英雄       | 101     |
| 蓝方 | 工程       | 102     |
| ...  | ...        | ...     |

## 扩展

### 自定义 UI 样式

修改 `RobotSelectionPanel.cs` 中的 `BuildUI()` 方法：

```csharp
// 修改背景颜色
var bgMask = CreateImage(transform, "BackgroundMask", new Color(0, 0, 0, 0.8f));

// 修改按钮颜色
redTeamButton = CreateTeamButton(..., new Color(0.9f, 0.1f, 0.1f), ...);
```

### 添加新的弹窗

继承 `PopupPanelBase` 基类：

```csharp
public class MyCustomPanel : PopupPanelBase
{
    protected override string PanelId => "MyCustomPanel";
    
    public static void Show()
    {
        var go = new GameObject("MyCustomPanel");
        go.AddComponent<MyCustomPanel>();
        DontDestroyOnLoad(go);
    }
}
```
