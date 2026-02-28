# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**真正好用的字幕翻译** - 为大规模媒体库用户打造。

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [Chinese](Readme.zh.md)

---

> **从 v1.x 升级?** 2.0.0 版本有重大变化 - 已移除 MySQL/MariaDB，设置不会迁移，需要全新开始。详见下文。

---

## 这是什么?

Lingarr on Steroids 是 [Lingarr](https://github.com/lingarr-translate/lingarr) 的分支。我们保留了核心思路(通过 Radarr/Sonarr 翻译字幕)，但重建了大部分后端并添加了大量 UI 改进。

开始是因为原版 Lingarr 在负载下有可靠性问题。我们需要一种当你有数千节目时不会崩溃的方案。

---

## 我们改了啥

### 后端

| 什么 | 为什么 |
|------|-----|
| 自定义翻译 Worker | Hangfire 在大队列时会卡死。我们自己写了 BackgroundService，支持 1-20 个并行 Worker、优先级队列、崩溃自动恢复。 |
| 默认 PostgreSQL | SQLite 在并发 Worker 下会锁死。PostgreSQL 的 MVCC 真的能用。我们保留 SQLite 作为小规模选项。 |
| 9状态翻译追踪 | 原版没法很好地回答"什么需要翻译?"。我们加了状态(Unknown, Pending, InProgress, Complete, Stale, AwaitingSource, NoSuitableSubtitles, Failed, Interrupted) 让查询变快。 |
| 多实例支持 | 有些人一个 Radarr/Sonarr 不够。现在可以连接多个 *arr 服务器到一个 Lingarr。 |
| 延迟修复 | 失败的行会带着上下文重试(默认10行)。当 AI 能看到前后文时，LLM 翻译质量显著提升。 |

### 字幕处理

- **FFmpeg 提取** - 从 MKV/MP4 容器中提取内嵌字幕
- **ASS/SSA 清理** - 删除绘图指令、音符号、音效占位符、URL
- **稀疏轨道过滤** - 跳过 <100 条的轨道(标识、歌曲)
- **外部字幕发现** - 找到你手动添加的字幕文件并追踪

### UI/UX

- **仪表板小部件** - 拖拽布局，通过 SignalR 实时更新
- **队列小部件** - 显示正在运行、已计划、失败的内容
- **翻译历史** - 图表+列表显示何时翻译了什么
- **API 使用追踪** - 显示每个服务支出的火花图
- **引导向导** - 首次设置引导你完成 Radarr/Sonarr 配置
- **主题支持** - 深色/浅色，CSS 变量，可匹配你的布局
- **7种语言** - EN, NL, DE, FR, ES, PL, ZH
- **离线检测** - 显示应用不可访问时

### 可靠性

- **孤立清理** - 检测升级何时重命名文件，你的 AI 翻译变成孤立文件
- **批量完整性检查** - 验证你库中的每个翻译
- **幽灵任务清理** - 删除卡住从未完成的任务
- **指数退避** - 带抖动的重试，不会反复敲失败的 API

---

## 支持的服务

**AI:**
- OpenAI (GPT)
- Anthropic (Claude)
- Google Gemini
- DeepSeek
- Chutes.ai (配额跟踪 + 自动暂停)
- LocalAI / Ollama (本地运行)

**云 API:**
- LibreTranslate
- DeepL
- Google Translate
- Bing Translate
- Yandex Translate
- Azure Translator

---

## 开始使用

### Docker 镜像标签

| 标签 | 描述 | 架构 |
|-----|------|------|
| `latest` | 最新稳定版 | `linux/amd64`, `linux/arm64` |
| `1.2.3` | 指定版本 | `linux/amd64`, `linux/arm64` |
| `main` | 开发版 | `linux/amd64`, `linux/arm64` |

> **注意:** 所有镜像支持 AMD64 (Intel/AMD) 和 ARM64 (Raspberry Pi, Apple Silicon)。

推荐 PostgreSQL。SQLite 适合小规模部署(单用户，<1000 媒体项)。

### PostgreSQL (推荐)

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

### SQLite (快速开始)

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    environment:
      - TZ=Your/Timezone
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

| 变量 | 描述 | 默认 |
|----------|-------------|--------|
| `ASPNETCORE_URLS` | 端口 | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` 或 `sqlite` | `postgresql` |
| `DB_HOST` | PostgreSQL 主机 | - |
| `DB_PORT` | PostgreSQL 端口 | `5432` |
| `DB_DATABASE` | 数据库名 | - |
| `DB_USERNAME` | 数据库用户 | - |
| `DB_PASSWORD` | 数据库密码 | - |
| `RADARR_URL` | 你的 Radarr URL | - |
| `RADARR_API_KEY` | Radarr API 密钥 | - |
| `SONARR_URL` | 你的 Sonarr URL | - |
| `SONARR_API_KEY` | Sonarr API 密钥 | - |

完整列表在 [Settings.MD](Settings.MD)。

---

## 致谢

原版 Lingarr 由 [rowanfuchs](https://github.com/lingarr-translate/lingarr) 创建。

图标: [Lucide](https://lucide.dev/icons)。  
字幕解析: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser)。  
翻译: LibreTranslate, GTranslate 库。

---

## 感谢

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)