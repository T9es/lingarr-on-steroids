# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**真正好用的字幕翻译工具** - 专为管理大规模媒体库的用户设计。

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Français](Readme.fr.md) | [Español](Readme.es.md) | [Chinese](Readme.zh.md)

---

> **从 v1.x 升级？** 版本 2.0.0 包含重大变更 - 已移除 MySQL/MariaDB，设置**不会**自动迁移，需要全新安装。详情请见下文。

---

## 这是什么？

Lingarr on Steroids 是 [Lingarr](https://github.com/lingarr-translate/lingarr) 的分支。我们保留了核心理念（通过 Radarr/Sonarr 获取并翻译字幕），但重写了大部分后端代码，并增加了许多用户界面 (UI) 改进。

发起这个项目是因为最初的 Lingarr 在高负载下存在可靠性问题。当你有成千上万部剧集和电影时，我们需要一个不会崩溃的系统。

---

## 我们改变了什么

### 后端

| 改进项 | 原因 |
|------|-----|
| 自定义翻译工作流 | Hangfire 在队列过大时容易崩溃。我们编写了自己的 BackgroundService，支持 1-20 个并行工作进程、优先级队列以及崩溃后的自动恢复功能。 |
| 默认使用 PostgreSQL | SQLite 在并发进程下容易发生锁死。PostgreSQL 的多版本并发控制 (MVCC) 表现优异。我们保留了 SQLite 作为小型环境的选项。 |
| 9 种状态追踪 | 原版无法直观地回答“哪些内容需要翻译？”。我们加入了 9 种状态（未知，待处理，处理中，已完成，已过期，等待源文件，无合适字幕，失败，已中断），让查询速度大幅提升。 |
| 多实例支持 | 一个 Radarr/Sonarr 实例对某些人来说是不够的。你现在可以将多个 *arr 服务器连接到一个 Lingarr。 |
| 延迟修复与重试 | 翻译失败的行会带上上下文（默认为前后 10 行）进行重试。当 AI 能看到前因后果时，大语言模型 (LLM) 的翻译质量会显著提升。 |

### 字幕处理

- **FFmpeg 提取** - 当字幕内嵌于 MKV/MP4 容器时，自动提取字幕
- **ASS/SSA 清理** - 移除绘图指令、音乐符号、音效标签和 URL 链接
- **强制/非完整字幕过滤** - 跳过条目 <100 行的字幕轨道（如仅包含招牌翻译或歌曲）
- **外部字幕检测** - 自动发现并追踪你手动添加的字幕文件

### UI/UX

- **仪表板小部件** - 拖放式布局，通过 SignalR 实现实时更新
- **任务队列小部件** - 显示正在运行、已排队和失败的任务
- **翻译历史记录** - 用图表和列表展示历史翻译数据
- **API 使用追踪** - 使用迷你走势图 (Sparklines) 显示各服务的花销
- **安装向导** - 首次启动向导会指导你完成 Radarr/Sonarr 的配置
- **主题支持** - 深色/浅色模式，使用 CSS 变量匹配你的桌面环境
- **7 种语言** - 英文, 荷兰文, 德文, 法文, 西班牙文, 波兰文, 中文
- **离线检测** - 当应用程序无法连接时显示离线状态

### 可靠性

- **清理孤立文件** - 当由于媒体文件升级重命名，导致之前的 AI 翻译文件变成孤立文件时，自动检测并处理
- **大规模完整性检查** - 验证媒体库中的每一份翻译
- **无响应任务清理** - 移除卡死且永远不会完成的任务
- **指数退避重试 (Backoff)** - 带有随机抖动 (jitter) 的重试机制，避免在 API 失败时被过度请求封禁

---

## 支持的服务

**人工智能 (AI):**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (带配额跟踪和自动暂停)
- LocalAI / Ollama (自托管)

**云端 API:**
- [LibreTranslate](https://libretranslate.com/)
- [DeepL](https://www.deepl.com/)
- [Google Translate](https://translate.google.com/)
- [Bing Translate](https://www.bing.com/translator)
- [Yandex Translate](https://translate.yandex.com/)
- [Azure Translator](https://www.microsoft.com/en-us/translator/business/translator-api/)

---

## 快速开始

### Docker 镜像标签

| 标签 | 描述 | 架构 |
|-----|-------------|---------------|
| `latest` | 最新稳定版 | `linux/amd64`, `linux/arm64` |
| `1.2.3` | 特定版本 | `linux/amd64`, `linux/arm64` |
| `main` | 开发测试版 | `linux/amd64`, `linux/arm64` |

推荐使用 PostgreSQL。SQLite 适用于小型环境（单用户，<1000 个媒体项目）。

> **注意：** 所有镜像均支持 AMD64 (Intel/AMD) 和 ARM64 (树莓派, Apple Silicon)。

### PostgreSQL (推荐)

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    container_name: lingarr
    environment:
      - TZ=Asia/Shanghai
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

### SQLite (快速上手)

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    environment:
      - TZ=Asia/Shanghai
      - DB_CONNECTION=sqlite
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

| 变量 | 描述 | 默认值 |
|----------|-------------|---------|
| `ASPNETCORE_URLS` | 端口 | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` 或 `sqlite` | `postgresql` |
| `DB_HOST` | PostgreSQL 地址 | - |
| `DB_PORT` | PostgreSQL 端口 | `5432` |
| `DB_DATABASE` | 数据库名称 | - |
| `DB_USERNAME` | 数据库用户名 | - |
| `DB_PASSWORD` | 数据库密码 | - |
| `RADARR_URL` | 你的 Radarr URL | - |
| `RADARR_API_KEY` | Radarr API 密钥 | - |
| `SONARR_URL` | 你的 Sonarr URL | - |
| `SONARR_API_KEY` | Sonarr API 密钥 | - |

完整列表请见 [Settings.MD](Settings.MD)。

---

## 鸣谢与版权

原版 Lingarr 开发者：[rowanfuchs](https://github.com/lingarr-translate/lingarr).

图标设计：[Lucide](https://lucide.dev/icons).  
字幕解析器：[AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
翻译支持：LibreTranslate，GTranslate 库.

---

## 感谢

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)
