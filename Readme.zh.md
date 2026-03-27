# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**面向真实 Radarr/Sonarr 媒体库的字幕翻译。**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [中文](Readme.zh.md)

---

> 本文内容已于 2026 年 3 月 27 日对照 `lingarr-translate/lingarr` 进行核对。上游项目在此日期之后仍可能继续变化。
>
> 从 v1.x 升级？2.0.0 包含破坏性变更。MySQL/MariaDB 已不再受支持，配置不会自动迁移，需要全新初始化。

---

## 这是什么？

Lingarr on Steroids 是 [Lingarr](https://github.com/lingarr-translate/lingarr) 的一个 fork。它保留了原始工作流：通过 Radarr 和 Sonarr 索引媒体、发现字幕轨道、使用支持的提供商进行翻译，并通过 Web UI 统一管理。

这个 fork 更关注队列可靠性、多实例媒体库、字幕修复能力，以及面向大型部署的运维可见性。

---

## 已验证的 fork 差异

### 后端与队列

| 领域 | 此 fork 中已验证的差异 |
|------|-------------------------|
| 自定义 translation worker | 翻译任务通过自定义 `BackgroundService` 和可配置并行 worker 执行，而不只是依赖 Hangfire 队列。 |
| 默认 PostgreSQL | PostgreSQL 是默认数据库，SQLite 仍适用于较小的部署。 |
| 媒体状态模型 | 媒体使用 9 个状态：`Unknown`、`NotApplicable`、`Pending`、`InProgress`、`Complete`、`Stale`、`AwaitingSource`、`NoSuitableSubtitles`、`Failed`。 |
| 多实例支持 | 电影和剧集记录 `SourceInstanceId`，因此一个 Lingarr 安装可以连接多个 Radarr 和 Sonarr 实例。 |
| 延迟修复 | 失败的字幕行可以带上下文重新尝试，使修复流程更稳健。 |

### 字幕处理

- FFmpeg 可以从嵌入式 MKV 和 MP4 轨道中提取文本字幕。
- ASS/SSA 清理会在翻译前移除绘图命令、音乐标记、占位效果和 URL。
- 少于 50 条对白的稀疏字幕轨道会被跳过。
- 外部字幕发现功能会识别并持续跟踪你手动添加的字幕文件。

### UI 与运维

- Onboarding 向导会引导首次配置 Radarr 和 Sonarr。
- 仪表盘组件支持拖拽布局，并通过 SignalR 提供实时更新。
- 任务队列和翻译历史组件提供了上游当前没有的可见性。
- API 使用组件展示调用次数、token、延迟、错误数和成功率。
- 客户端内置 11 个主题，而不只是明暗两种模式。
- UI 已翻译为英语、荷兰语、德语、法语、西班牙语、波兰语和简体中文。

### 可靠性

- 孤儿字幕清理可以检测重命名媒体后遗留下来的已翻译字幕文件。
- 批量完整性检查可以验证整个媒体库中的翻译字幕。
- Ghost job 保护会避免覆盖终态请求，并在重启后清理被中断的工作。
- 指数退避和延迟重新入队逻辑可降低对不稳定提供商的压力。
- 此 fork 中的 Chutes 集成包含额度感知控制和提供商专用逻辑。

---

## 支持的服务

这是此 fork 在 snapshot 日期上的兼容性列表。其中部分服务上游也已支持，因此这不是“fork 独有”的声明。

**AI：**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/)（包含额度跟踪与自动暂停）
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
