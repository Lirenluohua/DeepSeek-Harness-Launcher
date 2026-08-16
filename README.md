# DeepSeek Harness 启动器

DeepSeek Harness 的服务管理器（C# WPF 原生版，独立 exe，零外部依赖）。

## 功能

- 服务启停：启动/停止 dsh web 服务（自动探测捆绑环境或手动指定 dsh 路径）
- 启动反馈：启动中状态提示（琥珀色）、防重复点击、40 秒超时保护
- 启动诊断：端口被占用 / node 缺失 / 启动超时 均给出明确原因与指引
- 实时监控：CPU / 内存 / 端口 / PID / 运行时长
- 资源趋势图（60 秒，可折叠）
- 日志面板：关键字高亮、自定义高亮、清空、导出、自动轮转
- 深/浅主题（可跟随系统）
- 系统托盘：状态图标（运行彩色/停止暗灰）、菜单、双击恢复
- Chrome 应用模式打开网页（连接检测避免重复打开，不依赖窗口标题）
- 服务异常自动重启、状态通知（可静音）
- 端口可配置、开机自启、单实例唤醒、窗口大小/位置记忆
- 安全：监听地址暴露检测、显式绑定 127.0.0.1、安全操作日志、敏感设置确认
- 内置卸载管理（设置 → 卸载 / 命令行 `--uninstall`，自动清理控制面板条目）

## 目录结构

```
DeepSeek-Harness-Launcher/
├── src/launcher.cs          # 启动器全部源码（单文件）
├── build.bat                # 编译启动器
├── packaging/               # 一键安装器工程
│   ├── Setup.cs             # 安装向导源码（C# WPF）
│   ├── build-setup.bat      # 打包安装包脚本
│   └── README.md
├── DeepSeek Harness.ico     # 图标
└── logs/                    # 运行日志（不入库）
```

## 构建启动器

需要 Windows 10/11（自带 .NET Framework 4.8 与 csc 编译器）。

```bat
build.bat
```

输出：`DeepSeekHarnessLauncher.exe`

## 构建一键安装包

安装包（约 34MB 单文件）内嵌：启动器、Node.js 22.23.2、dsh 包。目标机器离线可用、无需管理员。

```bat
cd packaging
build-setup.bat
```

资源准备见 `packaging/README.md`（node.zip / dsh.zip 不入库，需按说明下载打包）。

输出：`packaging/DeepSeekHarness-Setup.exe`

## 安装包下载

Releases 页（https://github.com/Lirenluohua/DeepSeek-Harness-Launcher/releases）可直接下载最新安装包。

## 使用

双击 `DeepSeekHarnessLauncher.exe`（或安装包生成的桌面快捷方式）即可。
