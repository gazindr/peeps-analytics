# Analytics

Unity-пакет: менеджер аналитики и HTML-воронка загрузки.

## Установка в Unity

1. **Window → Package Manager**
2. Кнопка **+** → **Add package from git URL**
3. Вставь:

```
https://github.com/gazindr/peeps-analytics.git
```

4. **Add**

После импорта:
- **BetterAnalytics → Game Folder** — сверху **Host**, ниже папка игры. Хост изначально пустой: впиши только домен, например `peepsgames.com` (без `https://` и без `/Games`). Папка игры — имя проекта на сервере (`НазваниеИгры`). `/Games` и php-пути подставятся сами. Apply запишет оба значения в префаб/сцену и в `funnel.js`
- **Connect funnel.js in HTML** (в том же окне или в меню) — впишет в `index.html` шаблона первой строкой в `<head>`:

```html
<script src="./funnel.js"></script>
```

Так воронка стартует до Unity. Нужен WebGL Template проекта (`Assets/WebGLTemplates/...`, в Player Settings).

- **BetterAnalytics → Add Analytics object to scene** — если Host и Game Folder уже заданы, объект создастся сразу с ними

Фиксированная версия (чтобы пакет не уезжал с `main`):

```
https://github.com/gazindr/peeps-analytics.git#v1.4.0
```

## Что внутри

| Часть | Назначение |
| --- | --- |
| `AnalyticsManager` | heartbeat, playtime, FPS, кастомные события → `analytics.php` |
| `AnalyticsFunnel` | `SetFunnel("Level1", 1)` → `funnel.php` |
| `funnel.js` | HTML-воронка `start_loading` / `loaded` до старта Unity |
| `HtmlFunnel.jslib` | общий `player_id` у HTML и C# |

Вызовы из игры:

```csharp
AnalyticsManager.Instance.SendCustomEvent("ShopOpen");
AnalyticsFunnel.Instance.SetFunnel("Level1", "Start");
AnalyticsFunnel.Instance.SetFunnel("Level1", 1);
```
