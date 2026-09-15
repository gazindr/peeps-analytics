# Peeps Analytics

Unity-пакет: менеджер аналитики + HTML-воронка загрузки для [peepsgames.com](https://peepsgames.com).

## Установка в Unity

1. **Window → Package Manager**
2. Кнопка **+** → **Add package from git URL**
3. Вставь:

```
https://github.com/gazindr/peeps-analytics.git
```

4. **Add**

После импорта:
- **Peeps → Analytics → Game Folder** — впиши папку игры (`MegaCarGame`). Это же значение уйдёт в префаб/сцену и в `funnel.js`
- **Connect funnel.js in HTML** (в том же окне или в меню) — впишет в `index.html` шаблона первой строкой в `<head>`:

```html
<script src="./funnel.js"></script>
```

Так воронка стартует до Unity. Нужен WebGL Template проекта (`Assets/WebGLTemplates/...`, в Player Settings).

- **Peeps → Analytics → Add Analytics object to scene** — если Game Folder уже задан, объект создастся сразу с ним

Фиксированная версия (чтобы пакет не уезжал с `main`):

```
https://github.com/gazindr/peeps-analytics.git#v1.2.0
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

Внешних C# зависимостей нет. Playgama / мультиплеер подхватываются сами, если они есть в проекте.

## Сервер

На peepsgames для папки игры должны лежать `analytics.php` и `funnel.php`.  
CSV-вариант `analytics.php` (без MySQL) лежит в сэмпле пакета: Package Manager → Peeps Analytics → Samples.

Не клади в git пароли от базы.
