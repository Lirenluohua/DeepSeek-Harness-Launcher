# 原生多模态图像理解（Vision Patch）—— 改动说明

DeepSeek Harness Launcher 相关改动：让官方视觉模型
`deepseek-v4-flash-vision-exp` **原生读取图片**（真实图像字节），并让该能力在
dsh / 依赖升级后不丢失。

---

## 一、背景与问题

在本次改动前，dsh 中粘贴 / 上传图片会得到以下结果之一，而不是真正的图像输入：

- **「图片文件地址」文本** —— 图片被转成路径文本；
- **`![图片](/describe-image/raw/…)` 引用** —— 图片被改写成描述工具引用；
- 聊天里根本**没有原生图片附件**。

根本原因是三处独立的机制叠加，让视觉模型收不到真实的图片字节。

## 二、根因

| # | 位置 | 问题 |
|---|---|---|
| 1 | `@deepseek-ai/dsh-llm-deepseek`（adapter） | 硬编码 `inputModalities: ["text"]`，序列化器直接拒绝图片内容，model catalog schema 也不透传 `inputModalities` |
| 2 | `@deepseek-ai/dsh-host-apiproxy`（`buildModelCatalog`） | 构建前端模型目录时丢弃了 `inputModalities`，前端拿不到模型的图片能力 |
| 3 | `@linxin666/dsh-tool-describe-image`（第三方，随 `dsh-web-ui-all` 打包） | 浏览器端安装了 send-hook，把任何带图发送重写成纯文本描述引用，并替换原生附件按钮 |

## 三、修复方案

### 1. adapter 改用官方 Files API
`dsh-llm-deepseek/lib/index.js`：
- model catalog schema 新增 `inputModalities` 字段并透传；
- `modelInfo` 尊重模型声明的 `inputModalities`（未声明才默认 text-only）；
- 图片通过官方 **Files API** 上传（`POST /files` → `file_id`），消息里引用为
  `{type: "file", file_id}` —— 这是官方推荐方式，突破 32 MiB 内联上限（单图可到 64 MiB），
  且只在 user 消息里放图片（system / assistant 含图会按官方限制返回 400）。

### 2. buildModelCatalog 透传 inputModalities
`dsh-host-apiproxy/lib/index.js`：`buildModelCatalog` 生成的模型条目补上
`inputModalities`，让前端模型目录携带图片能力。

### 3. 禁用 @linxin666/dsh-tool-describe-image
`~/.dsh/profiles/web/cordis.patch.yml`：用 `{id: describe-image, disabled: true}` 禁用该插件。
> 注意：dsh 的 patch 解析（`dsh-app-boot/applyEntryPatches`）只认 `{id, ...overrides}` 格式，
> **不识别 `- disable:` 列表**（旧版 rc.6 语法），后者会被静默跳过。禁用实例 id 是
> `describe-image`（见 `dsh-web-ui-all/cordis.patch.yml`），不是 npm 包名。

### 4. settings.yaml 声明模型能力
`~/.dsh/settings.yaml` 给 `deepseek-v4-flash-vision-exp` 声明
`inputModalities: [text, image]`。

## 四、Launcher 持久化（防升级丢失）

### ① 启动自动写回
`src/launcher.cs` 新增 `ApplyVisionPatch()`，在 `StartServer()` **启动服务之前**调用：
- 以 `-ExecutionPolicy Bypass` 运行 `patches/dsh-vision/writeback.ps1`；
- 传 `-DshRoot <AppDir>\dsh`，同时覆盖**安装版** dsh（装在 `AppDir\dsh`）与
  **npx 版**（`npm-cache\_npx\...`）；
- 找不到 patch 时安全跳过，不影响服务启动。

### ② 安装包内置
- `packaging/build-setup.bat`：新增 `Compress-Archive` 打包 `patches/dsh-vision/*` 为
  `dsh-vision.zip`，并作为内嵌资源嵌入；
- `packaging/Setup.cs`：安装时把 `dsh-vision.zip` 解压到
  `<InstallDir>\patches\dsh-vision`（对齐 launcher 查找路径）；
- `packaging/.gitignore`：忽略 `dsh-vision.zip`（构建产物，同 node.zip / dsh.zip）。

### ③ Patch 源文件（`patches/dsh-vision/`）
| 文件 | 说明 |
|---|---|
| `dsh-llm-deepseek/lib/index.js` | 改好的 adapter（Files API 原生读图） |
| `dsh-host-apiproxy/lib/index.js` | 改好的 catalog（透传 `inputModalities`） |
| `writeback.ps1` | 自动写回脚本 |
| `README.md` | patch 内部说明 |

## 五、验证

- **adapter 端到端**：给 `deepseek-v4-flash-vision-exp` 发一张写有 "FILE 88" 的图，
  模型准确回读。
- **paste 判定**：`GET /modlens/paste?model=deepseek-v4-flash-vision-exp` 返回
  `{"takeover": false}`，即粘贴走原生，不再被转成文本。
- **组合禁用**：`composeEntries` 离线测试证明 `describe-image` 被写入
  `disabled: true`，配合 cordis-plugin-loader 跳过 disabled 条目，插件不激活。
- **真实 UI**：粘贴一张白马图，模型直接描述内容（真实图像输入，而非引用文本）。

## 六、升级注意

- 运行时改动（adapter / apiproxy / cordis.patch）在 dsh 升级后可能被覆盖，
  **launcher 每次启动会自动写回**，无需手动操作。
- 若目标是**分发给别人**：用 `build.bat` 构建 launcher、`build-setup.bat` 构建安装包，
  两者都已包含 vision patch 逻辑。

## 七、价格参考（官方）

`deepseek-v4-flash-vision-exp` 与 `deepseek-v4-flash` **同价**（最便宜档），仅
`deepseek-v4-pro` 为 3 倍。传给视觉模型的图片按尺寸换算成 token，与文本一并计费，
无额外加价。
