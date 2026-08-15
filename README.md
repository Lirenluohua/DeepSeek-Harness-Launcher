# DeepSeek Harness 启动器

DeepSeek Harness 的服务管理器（C# WPF 原生版，独立 exe，零外部依赖）。

## 功能

- 服务启停：启动/停止 dsh web 服务（自动探测或手动指定 dsh 路径）
- 实时监控：CPU / 内存 / 端口 / PID / 运行时长
- 资源趋势图（60 秒，可折叠）
- 日志面板：关键字高亮、自定义高亮、清空、导出
- 深/浅主题（可跟随系统）
- 系统托盘：状态图标（运行彩色/停止暗灰）、菜单、双击恢复
- Chrome 应用模式打开网页（自动检测避免重复打开）
- 服务异常自动重启、状态通知（可静音）
- 端口可配置、开机自启、单实例唤醒、窗口大小/位置记忆
- 安全：监听地址暴露检测、显式绑定 127.0.0.1、安全操作日志、敏感设置确认
- 内置卸载管理（设置 → 卸载）

## 构建

需要 Windows 10/11（自带 .NET Framework 4.8 与 csc 编译器）。

```bat
build.bat
```

输出：`DeepSeekHarnessLauncher.exe`

## 目录结构

```
launcher/
├── src/launcher.cs          # 全部源码（单文件）
├── build.bat                # 构建脚本
├── DeepSeek Harness.ico     # 图标
└── logs/                    # 运行日志（不入库）
```

## 使用

双击 `DeepSeekHarnessLauncher.exe`（或桌面快捷方式）即可。
