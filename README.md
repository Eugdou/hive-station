# Hive Station 🐝

一个 Windows 便携桌面工作台，吸附在屏幕边缘，鼠标悬停展开，离开自动收起。原网页版「Buddy's Command Center」原生重写为 C# + WPF，零云端依赖、零运行时安装。

## 特性

- **边缘吸附悬浮面板**：吸附屏幕左/右边缘，悬停展开、离开 0.3s 自动收起，不抢当前软件焦点
- **随手记**：全局速记，防抖自动保存 + 保存状态指示，抗崩溃不丢数据
- **今日待办**：按项目分组总览，行内编辑、完成、加急、删除（悬停才显示操作按钮）
- **待办详情**：内嵌引用块式详情区，多行自动保存，与任务稳定关联
- **稳定排序**：未完成紧急置顶 → 未完成 → 已完成，独立 `sort_order` 保证可预测
- **项目管理**：设置页内联增删改名（不弹窗），删除有任务的项需选择转移目标
- **双主题**：Linear 风格浅色/深色，紫强调，实时切换并持久化
- **便携发布**：自包含单文件，解压即用，无需安装 .NET，数据存在同级 `data/` 随文件夹走

## 数据可靠性

- JSON 快照 + 原子写（`.tmp` → `File.Move`）防半写损坏
- 写前备份 `.prev.json`，损坏时回滚链：主文件 → `.prev` → 备份 → 默认
- schema 版本化迁移
- 旧网页版导出 JSON 可导入（双格式、大小写不敏感）

## 运行要求

- Windows 10 1809+ / Windows 11（x64）
- Portable 版无需安装 .NET 运行时

## 构建

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o HiveStation-Portable
```

## 技术栈

C# + WPF（.NET 10），零额外 NuGet 依赖，手写 MVVM。
