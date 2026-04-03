# Hgerman Content Factory

AI-powered short-form video generation and publishing platform for YouTube, Instagram and TikTok.
Supports **English, German, Spanish, French, Italian and Polish** content across 15 niche categories.

---

## Architecture

```
HgermanContentFactory.sln
├── HgermanContentFactory.Core          → Entities, DTOs, Interfaces, Enums
├── HgermanContentFactory.Infrastructure → DbContext, Repositories, Services
│   ├── Services/AI/ContentGenerationService.cs   (OpenAI GPT-4o)
│   └── Services/YouTube/YouTubeService.cs        (Google YouTube Data API v3)
├── HgermanContentFactory.Web           → ASP.NET Core MVC + SignalR + Hangfire
└── HgermanContentFactory.Worker        → Background jobs (Scheduler + Trend Refresh)
```

---

## Quick Start

### 1. Prerequisites

- Visual Studio 2022 (v17.8+) or VS Code with C# extension
- .NET 8 SDK
- Azure SQL Database (or local SQL Server)
- OpenAI API key (GPT-4o)
- Google Cloud project with YouTube Data API v3 enabled

---

### 2. Database Setup

Run the SQL schema script against your Azure SQL `HgermanAppsDB` database:

```
HgermanContentFactory.Infrastructure/Data/CF_Schema.sql
```

This creates all **CF_** prefixed tables, views, indexes and stored procedures.

Then update the connection string in both `appsettings.json` files:

```json
"ConnectionStrings": {
  "HgermanAppsDB": "Server=tcp:YOUR_SERVER.database.windows.net,1433;..."
}
```

---

### 3. Google YouTube OAuth Setup

1. Go to [console.cloud.google.com](https://console.cloud.google.com)
2. Create a project → **APIs & Services** → Enable **YouTube Data API v3**
3. **Credentials** → Create **OAuth 2.0 Client ID** (Web Application)
4. Add Authorized redirect URI:
   ```
   https://localhost:5001/Channels/YouTubeCallback
   https://yourdomain.com/Channels/YouTubeCallback
   ```
5. Copy Client ID and Secret to `appsettings.json`:
   ```json
   "YouTube": {
     "ClientId":     "YOUR_CLIENT_ID",
     "ClientSecret": "YOUR_CLIENT_SECRET"
   }
   ```

---

### 4. OpenAI Setup

```json
"OpenAI": {
  "ApiKey": "sk-YOUR_OPENAI_API_KEY",
  "Model":  "gpt-4o"
}
```

The AI generates:
- Video scripts (in the target language, niche-optimized)
- Click-worthy titles
- SEO descriptions
- 18 viral hashtags
- Trend topic discovery (10 topics per language/niche combo)

---

### 5. Run the Application

```bash
# Restore packages
dotnet restore

# Run web app
cd HgermanContentFactory.Web
dotnet run

# Run background worker (separate terminal)
cd HgermanContentFactory.Worker
dotnet run
```

Or in Visual Studio: right-click solution → **Set Startup Projects** → Multiple startup projects:
- `HgermanContentFactory.Web`  → Start
- `HgermanContentFactory.Worker` → Start

---

## Workflow

```
1. Create Channel
   └─ Set language + niche + daily video target

2. Connect YouTube
   └─ OAuth flow → tokens stored in CF_Channels

3. Discover Trends
   └─ AI analyzes trending topics → stored in CF_TrendTopics

4. Generate Video
   └─ AI generates title + script + description + hashtags
   └─ Status: Pending → ScriptReady

5. Render Video  ← integrate your video renderer here
   └─ Status: ScriptReady → Rendered
   └─ Set VideoFilePath to the rendered .mp4

6. Publish
   └─ Upload to YouTube via API
   └─ Status: Rendered → Published
   └─ CF_PublishLogs records every attempt
```

---

## Database Objects (CF_ prefix)

| Object | Type | Description |
|--------|------|-------------|
| CF_Channels | Table | One row per YouTube/IG/TikTok channel |
| CF_Videos | Table | Every generated video |
| CF_TrendTopics | Table | AI-discovered trending topics |
| CF_Campaigns | Table | Batches of videos |
| CF_Schedules | Table | Daily publishing schedules |
| CF_ApiKeys | Table | Encrypted API keys |
| CF_PublishLogs | Table | Publish audit log |
| CF_AnalyticsSnapshots | Table | Daily stats snapshots |
| CF_V_ChannelSummary | View | Aggregated channel stats |
| CF_V_DashboardStats | View | Single-row KPI view |
| CF_V_VideoDetails | View | Videos with channel + trend info |
| CF_V_WeeklyPublishChart | View | Last 7 days publish counts |
| CF_SP_GetDueScheduledVideos | SP | Videos ready to publish |
| CF_SP_UpdateChannelStats | SP | Recalculate channel aggregates |
| CF_SP_CleanupExpiredTrends | SP | Deactivate stale trends |

---

## Supported Languages

| Code | Language |
|------|----------|
| 1 | English |
| 2 | German (Deutsch) |
| 3 | Spanish (Español) |
| 4 | French (Français) |
| 5 | Italian (Italiano) |
| 6 | Polish (Polski) |

---

## Integrating a Video Renderer

The `RenderAsync` method in `VideoGenerationService.cs` is the integration point.
Replace the placeholder with your preferred renderer:

- **FFmpeg** — generate video from images + TTS audio
- **Remotion** — React-based video generation
- **Pictory / InVideo API** — cloud video generation
- **ElevenLabs** — text-to-speech for narration

After rendering, set `video.VideoFilePath` to the `.mp4` file path before calling `PublishAsync`.

---

## Hangfire Dashboard

Access at: `https://yourdomain/hangfire`

Recurring jobs:
- `enqueue-daily-videos` — every hour
- `refresh-trends` — every 6 hours
