# dsh Vision Patch — native multimodal image reading for deepseek-v4-flash-vision-exp

## What this is

Makes the DeepSeek official vision model read pasted/uploaded images **natively**
(real image bytes), instead of dsh reducing them to a text reference or a file path.

Three things were wrong and are fixed by these two patches (+ one config disable):

1. **`dsh-llm-deepseek`** hard-coded `inputModalities: ["text"]` and its serializer
   rejected image content. The patch makes the catalog `inputModalities` flow through
   and uploads the image via the **official Files API** (`POST /files` → `file_id`,
   referenced as `{type:"file", file_id}`), so the model reads the picture itself.
2. **`dsh-host-apiproxy`** (`buildModelCatalog`) dropped `inputModalities` when
   building the model catalog for the frontend. The patch passes it through so the
   selected model is recognised as image-capable.
3. **`@linxin666/dsh-tool-describe-image`** (bundled with `dsh-web-ui-all`) installed a
   browser send-hook that rewrote every image-bearing send into a plain-text reference
   (`![图片](/describe-image/raw/…)`) and replaced the native attachment button.
   It is disabled via `~/.dsh/profiles/web/cordis.patch.yml`:

   ```yaml
   - id: describe-image
     disabled: true
   ```

   The `settings.yaml` entry for the model already declares
   `inputModalities: [text, image]`.

## Files in this directory

```
dsh-vision/
├── dsh-llm-deepseek/lib/index.js      # patched adapter (Files API)
├── dsh-host-apiproxy/lib/index.js     # patched catalog (inputModalities)
├── writeback.ps1                       # restore both patches into the install
└── README.md                           # this file
```

## Why a patch (persistence)

The launcher's dsh update replaces the **`@deepseek-ai/dsh`** main package only; it does
**not** touch the two dependency packages above, so after a plain dsh update these
patches survive. They are **overwritten** when `dsh-llm-deepseek` / `dsh-host-apiproxy`
themselves are upgraded — e.g. `npm install`, `pnpm install`, or a `dsh plugin` /
reinstall of the profiles.

## How to restore after an upgrade

Run the write-back script with the execution-policy bypass:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "D:\DeepSeek_harness\launcher\patches\dsh-vision\writeback.ps1"
```

It scans `npm-cache\_npx\*\node_modules\@deepseek-ai\...`, backs up each target once as
`<file>.bak-vision`, and writes the patch back. A target already carrying the patch is
reported as `[ok] already patched` and left untouched.

## Optionally integrate into the launcher

To make this automatic, call the same command from the launcher **before starting the
web service** (so every start re-applies the patch). Because the command needs an
execution-policy bypass, launch it through `powershell.exe -ExecutionPolicy Bypass -File`.
Only re-run the write-back when the target does not already contain the patch marker
(the script self-guards), so it is cheap to call on every start.
