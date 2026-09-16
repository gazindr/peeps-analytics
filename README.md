# Analytics

Unity-пакет: менеджер аналитики, remote config и HTML-воронка загрузки.

## Установка в Unity

1. **Window → Package Manager**
2. Кнопка **+** → **Add package from git URL**
3. Вставь:

```
https://github.com/gazindr/peeps-analytics.git
```

4. **Add**

После импорта:
- **BetterAnalytics → Game Folder** — сверху **Host**, ниже папка игры. Хост изначально пустой: впиши только домен, например `example.com`. Папка игры — имя проекта на сервере (`НазваниеИгры`). Apply запишет оба значения в префаб/сцену и в `funnel.js`
- **BetterAnalytics → Add Analytics object to scene**
- **BetterAnalytics → Add Remote Config object to scene** — Host и Game Folder подставятся из аналитики
- **Connect funnel.js in HTML** (в том же окне или в меню) — впишет в `index.html` шаблона первой строкой в `<head>`:

```html
<script src="./funnel.js"></script>
```

Так воронка стартует до Unity. Нужен WebGL Template проекта (`Assets/WebGLTemplates/...`, в Player Settings).

## Что внутри

| Часть | Назначение |
| --- | --- |
| `AnalyticsManager` | heartbeat, playtime, FPS, кастомные события → `analytics.php` |
| `AnalyticsFunnel` | `SetFunnel("Level1", 1)` → `funnel.php` |
| `funnel.js` | HTML-воронка `start_loading` / `loaded` до старта Unity |
| `HtmlFunnel.jslib` | общий `player_id` у HTML и C# |
| `RemoteConfigLoader` | только получает флаги |
| `RemoteConfigActions` | закрытый скрипт игры: читает лоадер и применяет флаги |

Вызовы аналитики:

```csharp
AnalyticsManager.Instance.SendCustomEvent("ShopOpen");
AnalyticsFunnel.Instance.SetFunnel("Level1", "Start");
AnalyticsFunnel.Instance.SetFunnel("Level1", 1);
```

## Remote Config

Два скрипта. Не смешивай их.

1. `RemoteConfigLoader` — пакетный. Качает флаги. Игровую логику сюда не пиши. Host и Game Folder берутся из аналитики.
2. `RemoteConfigActions` — закрытый скрипт проекта (`Assets/BetterAnalytics/RemoteConfigActions.cs`). Подписывается на лоадер и интерпретирует флаги для игры.

Кнопка **Add Remote Config object to scene** добавит оба компонента и создаст `RemoteConfigActions`, если его ещё нет.

В `RemoteConfigActions`:

```csharp
void Apply()
{
    string version = RemoteConfigLoader.GetString("Version", "0.0.0");
    int timeScale = RemoteConfigLoader.GetInt("TimeScale", 100);
    bool showChat = RemoteConfigLoader.GetBool("ShowChat", true);
}
```

Локальные дефолты флагов задаются в инспекторе у `RemoteConfigLoader`.
