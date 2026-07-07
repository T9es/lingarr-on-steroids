# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**面向真实 Radarr/Sonarr 媒体库的字幕翻译。**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Français](Readme.fr.md) | [Español](Readme.es.md) | [中文](Readme.zh.md)

---

>
> 这份 README 反映的是我们这个 fork，以及上游 Lingarr 在 2026 年 6 月 29 日时的状态。之后如果上游继续变化，这里的部分细节可能会慢慢落后。
>
> 从 v1.x 升级？2.0.0 包含破坏性变更。MySQL/MariaDB 已不再受支持，配置不会自动迁移，需要全新初始化。
>
> 从 v2.x 升级到 v3.0.0？请阅读 [CHANGELOG](CHANGELOG.md) 中的迁移说明。Schedule 页面已重命名为 Tasks。Onboarding 向导、可配置的作业调度以及后期翻译质量门都发生了变化。CrofAI 加入了受支持的 AI 提供商列表。左下角的版本徽章现在会显示真实的开发版本号，而不是 "Dev Build"。

---

## v3.0.0 新特性

v3 版本相比 v2.5.0 是一次重大变更。如果你只读一节，请读这一节。

- **版本管理改为基于 Git。** Assembly 版本号现在在构建时通过 `git describe` 解析，Docker 构建也会转发一个 `VERSION` 参数。发布新版本只需打 `v3.0.0` 标签并推送，不再需要手动修改 `Lingarr.Core.csproj`。
- **Dev Build 徽章显示真实版本。** 左下角的徽章现在显示 `Dev <version>`（例如 `Dev 3.0.0-216-g39ae09b2`），不再只是泛用的 `Dev Build` 文字。
- **CrofAI、NanoGPT、Chutes.ai 和 LocalAI** 加入为新的受支持翻译后端，均带有配额跟踪。
- **位图字幕 OCR 支持。** DVD/VobSub、PGS 等基于图像的字幕轨会先经过 OCR 转成文本，再像其他来源一样被翻译。新增 `OcrPending` 和 `OcrBlocked` 两个状态覆盖 OCR 生命周期。
- **按提供商的熔断器。** 如果某个提供商开始大量返回 5xx，熔断器会打开并暂停请求，避免在故障期间消耗你的 API 配额。
- **暂停翻译的恢复机制。** 来自提供商的 429 错误（例如 Gemini 限流）不会再直接终止翻译。Worker 会保留槽位，限制解除后自动继续。
- **翻译后质量门。** 批次完成后，剩余段落会被打分。UI 支持对超出容差的条目进行复核、编辑、接受或拒绝，并提供 Requeue All / Dismiss All 的批量操作。
- **内联字幕对比与编辑。** 将原始字幕和翻译字幕并排对比，逐行编辑，并直接在仪表盘中批准或拒绝。
- **字幕完整性检查。** 对整个媒体库进行自动化的质量验证，支持可配置的检查计划。
- **自定义翻译源。** 通过 Custom Sources 设置页面接入你自己的翻译 API 或自托管 LLM 提供商。
- **自动源语言模式。** 可以按 cue 自动检测源语言，使用 NLLB（FLORES-200 spBLEU）、LLM 层级比较和语言族启发式。开关在 onboarding 和源语言设置中。
- **新版 Tasks 页面提供可配置的作业调度。** 每个 Hangfire 和翻译作业都有独立的启用开关和 cron 表达式。Tasks 页面是 Schedule 页面的重命名和重新设计版本，使用共享的 CardComponent、1/2/3 列响应式网格、显式的加载和空状态、以及修正后的 SignalR 清理。Limits 卡片上原来的 automation 块已移除。
- **可配置的嵌入和语言检测，并提供新的 UI。** 前端设置支持 MKV 嵌入行为、未打标签流的语言检测，以及请求重试上限。
- **MKV 嵌入回退应对过长输出路径。** 如果翻译后字幕路径超过常见文件系统限制（长动漫文件名是典型例子），翻译结果会直接嵌入回原始 MKV。
- **Upload workspace 移入 translations。** Upload Workspace 现在作为 Translations 页面里的一个标签页，减少跨页面跳转。
- **仪表盘无限滚动、Completed Translation Compare Viewer、API 用量组件增强。** 这些体验改进在大体量媒体库中尤为有用。
- **GitHub Issue 模板** 涵盖 bug、feature 和 setup，存放在 `.github/ISSUE_TEMPLATE/`。提交 issue 时请使用这些模板。

完整迁移说明见 [CHANGELOG.md](CHANGELOG.md#migration-notes-for-3x-v300)。

---
---

## 这是什么？

Lingarr on Steroids 是 [Lingarr](https://github.com/lingarr-translate/lingarr) 的一个 fork。它保留了原始工作流：通过 Radarr 和 Sonarr 索引媒体、发现字幕轨道、使用支持的提供商进行翻译，并通过 Web UI 统一管理。

这个 fork 更关注队列可靠性、多实例媒体库、字幕修复能力，以及面向大型部署的运维可见性。

---

## 我们改了什么

### 后端与队列

| 领域 | 我们这个 fork 里有什么不同 |
|------|--------------------------|
| 自定义 translation worker | 翻译任务通过自定义 `BackgroundService` 和可配置并行 worker 执行，而不只是依赖 Hangfire 队列。 |
| 默认 PostgreSQL | PostgreSQL 是默认数据库，SQLite 仍适用于较小的部署。 |
| 11 状态媒体模型 | 媒体使用 11 个状态来跟踪翻译进度，覆盖 OCR 生命周期：`Unknown`、`NotApplicable`、`Pending`、`InProgress`、`Complete`、`Stale`、`NoSuitableSubtitles`、`Failed`、`AwaitingSource`、`OcrPending`、`OcrBlocked`。决策逻辑在 `MediaStateService` 中。 |
| 多实例支持 | 电影和剧集记录 `SourceInstanceId`，因此一个 Lingarr 安装可以连接多个 Radarr 和 Sonarr 实例。 |
| 延迟修复 | 失败的字幕行可以带上下文重新尝试，使修复流程更稳健。 |
| 按提供商熔断器 | 单例熔断器按翻译提供商跟踪失败次数，越过阈值后自动熔断。 |
| 暂停翻译的恢复 | 遇到提供商 429 限流（如 Gemini）时，请求会在保留 worker 槽位的情况下暂停，限制解除后自动恢复。 |
| 翻译后质量门 | 批次结束后，对剩余段落按可配置容差打分。UI 可编辑或拒绝，默认开启并在设置中提供开关。 |

### 字幕处理

- FFmpeg 可以从嵌入式 MKV 和 MP4 轨道中提取文本字幕。
- ASS/SSA 清理会在翻译前移除绘图命令、音乐标记、占位效果和 URL。
- 少于 50 条对白的稀疏字幕轨道会被跳过。
- 外部字幕发现功能会识别并持续跟踪你手动添加的字幕文件。
- 位图字幕轨（DVD/VobSub、PGS 等）会先经过 OCR 转为文本，再像其他来源一样被翻译。
- ASS 完整性检查会捕获泄露的标签片段，避免翻译提示把绘图命令当成对白。
- 翻译后路径过长时会改回嵌入到原 MKV，而不是写在媒体文件旁。

### UI 与运维

- Onboarding 向导会引导首次配置 Radarr 和 Sonarr。
- 仪表盘组件支持拖拽布局，并通过 SignalR 提供实时更新。
- 任务队列和翻译历史组件提供了上游当前没有的可见性。
- API 使用组件展示调用次数、token、延迟、错误数和成功率。
- 失败会进入质量门审计，你可以就地编辑出问题的 cue 并接受或拒绝。失败的批次支持批量重排队或批量忽略。
- 完成后可使用翻译对比查看器并排查看源文本与翻译结果。
- 仪表盘历史组件改为无限滚动，减少大媒体库下的分页操作。
- Upload Workspace 现在作为 Translations 页面内的一个标签页，减少跨页面跳转。Custom Sources 仍保留为设置中的独立入口。
- 重新设计的 Tasks 页面（原 Schedule 页面）提供可配置的作业调度，包含按作业开关、cron 表达式、共享 CardComponent、响应式网格以及显式的加载和空状态。
- 客户端内置 11 个主题，而不只是明暗两种模式。
- UI 已翻译为英语、荷兰语、德语、法语、西班牙语、波兰语和简体中文。

### 可靠性

- 孤儿字幕清理可以检测重命名媒体后遗留下来的已翻译字幕文件。
- 批量完整性检查可以验证整个媒体库中的翻译字幕。
- Ghost job 保护会避免覆盖终态请求，并在重启后清理被中断的工作。
- 指数退避和延迟重新入队逻辑可降低对不稳定提供商的压力。
- 暂停翻译的恢复机制会在遇到限流时保留 worker 槽位，限制解除后自动恢复。
- AI 提供商的静默 token 流式响应可降低长翻译的首 token 延迟。
- 自有翻译队列尊重媒体优先级，避免低优先级翻译阻塞整个队列。
- Chutes、NanoGPT 和 CrofAI 集成都包含配额感知、对应提供商的特定控制以及此 fork 中的 UI。

---

## 支持的服务

这是我们这个 fork 当前可用的兼容性列表。其中有些服务上游现在也已经支持，所以这里想表达的是“能不能用”，不是“只有我们有”。

**AI：**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/)（包含额度跟踪与自动暂停）
- [NanoGPT](https://nano-gpt.com/)（包含订阅用量、保留额度与自动暂停）
- [CrofAI](https://crof.ai/)（仅追踪 credits 用量；当 credits 余额降为零时自动暂停翻译）
- LocalAI / Ollama（自托管）


**云 API：**
- [LibreTranslate](https://libretranslate.com/)
- [DeepL](https://www.deepl.com/)
- [Google Translate](https://translate.google.com/)
- [Bing Translate](https://www.bing.com/translator)
- [Yandex Translate](https://translate.yandex.com/)
- [Azure Translator](https://www.microsoft.com/en-us/translator/business/translator-api/)

---

## 快速开始

### Docker 镜像标签

| 标签 | 说明 | 架构 |
|------|------|------|
| `latest` | 最新稳定版本 | `linux/amd64`, `linux/arm64` |
| `1.2.3` | 指定版本 | `linux/amd64`, `linux/arm64` |
| `main` | 开发构建版本 | `linux/amd64`, `linux/arm64` |

推荐使用 PostgreSQL。SQLite 适合较小的部署（单用户、少于 1000 个媒体项目）。

> 注意：所有镜像都支持 AMD64（Intel/AMD）和 ARM64（Raspberry Pi、Apple Silicon）。

### PostgreSQL（推荐）

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    container_name: lingarr
    environment:
      - TZ=Your/Timezone
      - DB_CONNECTION=postgresql
      - DB_HOST=postgres
      - DB_PORT=5432
      - DB_DATABASE=lingarr
      - DB_USERNAME=lingarr
      - DB_PASSWORD=your_secure_password
    volumes:
      - ./movies:/movies
      - ./tv:/tv
      - ./config:/app/config
    ports:
      - "9876:9876"
    restart: unless-stopped
    depends_on:
      postgres:
        condition: service_healthy

  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: lingarr
      POSTGRES_USER: lingarr
      POSTGRES_PASSWORD: your_secure_password
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U lingarr -d lingarr"]
      interval: 10s
      timeout: 5s
      retries: 5
    restart: unless-stopped

volumes:
  postgres_data:
```

### SQLite（快速开始）

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    environment:
      - TZ=Your/Timezone
      - DB_CONNECTION=sqlite
      - SQLITE_DB_PATH=lingarr.db
    volumes:
      - ./movies:/movies
      - ./tv:/tv
      - ./config:/app/config
    ports:
      - "9876:9876"
    restart: unless-stopped
```

---

## 配置

| 变量 | 说明 | 默认值 |
|------|------|--------|
| `TZ` | 容器时区 | - |
| `ASPNETCORE_URLS` | HTTP 监听地址 | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` 或 `sqlite` | `postgresql` |
| `SQLITE_DB_PATH` | `/app/config` 中的 SQLite 文件名 | `local.db` |
| `DB_HOST` | PostgreSQL 主机 | - |
| `DB_PORT` | PostgreSQL 端口 | `5432` |
| `DB_DATABASE` | 数据库名称 | - |
| `DB_USERNAME` | 数据库用户名 | - |
| `DB_PASSWORD` | 数据库密码 | - |
| `MAX_PARALLEL_TRANSLATIONS` | 自定义翻译 worker 的启动并发值 | `1` |
| `MAX_CONCURRENT_JOBS` | Hangfire 同步和系统队列的 worker 数量 | `5` |
| `RADARR_URL` | 你的 Radarr URL | - |
| `RADARR_API_KEY` | Radarr API Key | - |
| `SONARR_URL` | 你的 Sonarr URL | - |
| `SONARR_API_KEY` | Sonarr API Key | - |

完整环境变量参考见 [Settings.MD](Settings.MD)。

---

## 致谢与版权

原始 Lingarr 作者：[rowanfuchs](https://github.com/lingarr-translate/lingarr)。

图标： [Lucide](https://lucide.dev/icons)。  
字幕解析： [AlexPoint](https://github.com/AlexPoint/SubtitlesParser)。  
翻译： LibreTranslate、GTranslate 库。

---

## 感谢

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)
