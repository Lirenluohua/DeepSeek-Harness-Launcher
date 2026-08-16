# DeepSeek Harness 安装器

一键安装包构建工程（C# WPF 向导，单文件分发）。

## 产物

`DeepSeekHarness-Setup.exe`（约 34MB，单文件）——内嵌：启动器、图标、Node.js 22.23.2、dsh 包。目标机器**离线可装、无需管理员、装完即用**。

## 构建

```bat
build-setup.bat
```

## 资源准备

- `node.zip`：Node.js win-x64 单文件包
  ```
  curl -L -o node.zip https://npmmirror.com/mirrors/node/v22.23.2/node-v22.23.2-win-x64.zip
  ```
- `dsh.zip`：dsh 包（`node_modules/@deepseek-ai/dsh` 打 zip，zip 内结构含 node_modules 顶层）

## 卸载

- 设置 → 卸载（启动器内）
- 控制面板「程序和功能」→ DeepSeek Harness 服务管理器（调用 `DeepSeekHarnessLauncher.exe --uninstall`）
