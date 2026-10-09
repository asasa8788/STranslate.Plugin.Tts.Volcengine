# STranslate 火山翻译 TTS 插件

基于火山翻译（Volcengine）Chrome 扩展内部语音合成接口的 STranslate TTS 插件。**无需 API Key、无需签名**，安装即可用。

## 安装

1. 从 [Releases](https://github.com/asasa8788/STranslate.Plugin.Tts.Volcengine/releases) 下载最新的 `.spkg`
2. STranslate → **设置** → **插件** → **安装插件**
3. 选择 `.spkg` 并重启 STranslate
4. 在 TTS 服务中添加 **火山 TTS**

## 音色怎么选

设置页只有一个关键选项：**音色**。

- **自动（跟随文本语言）**（默认）：插件不向服务端传 speaker，由服务端按文本语言自动选择音色。中文用中文嗓、英文用英文嗓、日文用日文嗓，无需任何配置。
- **指定音色**：选定后所有语言都固定使用该音色，不管文本是什么语言。

列表里内置 33 个已实测可用的音色，可直接在下拉里输入新的音色 ID 并回车添加，也可以点右侧垃圾桶删除。

## 支持的语言

自动音色模式下正常出声：中文、英语、日语、韩语、法语、西班牙语、德语、意大利语、葡萄牙语、土耳其语、越南语、马来语、印尼语。

**暂不支持**：俄语、阿拉伯语 —— 火山对应的唯一音色（BV068 / BV570）已从服务端下线。朗读这两种语言时插件会给出明确提示，不会静默失败。

> 韩语目前只剩男声 `kr_male_gye`（女声 BV059 已下线）。

## 超长文本

服务端单次请求硬上限 **2000 字符**（实测 2000 通过、2001 拒绝）。超过上限时插件按句末标点切分，分段合成后依次播放。

## 音频格式

服务端固定返回带 ID3 头的 **MP3**（24 kHz 单声道）。插件显式声明 `AudioFormat.Mp3` 交给宿主播放，不依赖格式猜测。

## 接口说明

```http
POST https://translate.volcengine.com/crx/tts/v1/
Content-Type: application/json

{ "text": "要朗读的文本" }              # 自动音色
{ "text": "...", "speaker": "zh_male_zhubo" }   # 指定音色
```

请求体只接受 `text` / `speaker` / `language` 三个字段，**多传任何字段都会返回 HTTP 400**。因此语速、音调、音量均不可调。

响应：

```json
{ "audio": { "duration": 0, "data": "<base64 mp3>" }, "base_resp": { "status_code": 0, "status_message": "" } }
```

## 本地编译

```powershell
dotnet build .\STranslate.Plugin.Tts.Volcengine\STranslate.Plugin.Tts.Volcengine.csproj -c Release
```

需要 .NET 10 SDK。Release 编译会在 `.artifacts/plugins/` 下生成 `.spkg`。

## 许可证

[MIT](LICENSE)
