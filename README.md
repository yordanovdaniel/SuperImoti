# SuperImoti

Уеб приложение на **ASP.NET Core MVC (.NET 10)**, което показва обявите за имоти в София, събрани от imot.bg със
собствен скрейпър, и позволява на регистрираните потребители да публикуват, редактират и изтриват свои обяви.

## Функционалности

- **Обяви** — търсене по ключови думи, филтри (сделка, категория, вид имот, квартал, цена, площ, особености, само със
  снимки), подреждане, страниране и **експорт в CSV**.
- **Страница на обява** — галерия, описание, особености, контакт, история на цената, подобни обяви и възможни
  дубликати (същият имот, публикуван от няколко агенции).
- **Пълен CRUD за обяви** — публикуване, редактиране и изтриване на собствени обяви с **качване на снимки**;
  администраторът може да редактира и изтрива всички обяви.
- **Квартали** — статистика по квартали; администраторът може да добавя, преименува и изтрива квартали.
- **Агенции** — профил на всяка агенция с нейните обяви.
- **Статистика** — интерактивна таблица на **Vue.js 3**, която зарежда данните от JSON адрес (`/Stats/Districts`).
- **Потребители** — регистрация, вход, профил (ASP.NET Core Identity), **любими обяви** и **коментари**.
- **Имейл известия** — абонираните потребители получават имейл при всяка нова обява.
- **Администрация** — синхронизация с базата на скрейпъра (във фонов режим), обобщени данни, нови потребители.

## Технологии

| Слой | Технология |
|---|---|
| Платформа | .NET 10, ASP.NET Core MVC, Razor изгледи |
| Данни | Entity Framework Core 10, SQLite, миграции |
| Потребители | ASP.NET Core Identity (роля „Administrator“) |
| Интерфейс | Bootstrap 5.3, Bootstrap Icons, jQuery Validation (unobtrusive) |
| JavaScript | Vue.js 3 (страница „Статистика“), малки подобрения в `wwwroot/js/site.js` |
| Тестове | xUnit, WebApplicationFactory (интеграционни тестове) |

## Стартиране

1. Инсталирайте **.NET 10 SDK** (`winget install Microsoft.DotNet.SDK.10`).
2. В `SuperImoti.Web/appsettings.Development.json` задайте `ScraperData:SourceFolder` — папката на скрейпъра,
   в която са `imot_imot.db` и `images/` (по подразбиране `C:\Users\Dani\Scrapers\CSharp\Imot.Scraper\imot.bg`).
3. Стартирайте:

   ```bash
   dotnet run --project SuperImoti.Web --launch-profile http
   ```

4. Отворете <http://localhost:5080>.

При първо стартиране базата `SuperImoti.Web/App_Data/superimoti.db` се създава автоматично от миграциите, а обявите
се импортират **във фонов режим** (около минута). Докато тече импортът, сайтът работи и показва лента с прогреса.
При всяко следващо стартиране се прави бърза синхронизация — добавят се само новите и променените обяви.

**Администратор:** имейлът е в `AdminUser:Email` (`appsettings.Development.json`), а паролата **не е в хранилището** —
пази се в .NET User Secrets на компютъра. Задайте я веднъж (поне 6 символа, с малка буква и цифра) и стартирайте сайта —
акаунтът се създава автоматично:

```bash
dotnet user-secrets set "AdminUser:Password" "<парола>" --project SuperImoti.Web
```

Вижте зададената парола с `dotnet user-secrets list --project SuperImoti.Web`.

**Без данните на скрейпъра** сайтът също работи: кварталите, видовете имоти и особеностите се зареждат от
`Data/Seed/lookups.json`, така че потребителите могат веднага да публикуват свои обяви.

**Имейли:** ако `Email:SmtpHost` е празно, имейлите се записват като `.eml` файлове в `App_Data/mail`.
За истинско изпращане попълнете SMTP настройките в секцията `Email`, а паролата на SMTP акаунта задайте с
`dotnet user-secrets set "Email:Password" "<парола>" --project SuperImoti.Web`.

## Как са покрити изискванията

| Изискване | Реализация |
|---|---|
| Начална страница | `HomeController.Index` — търсачка, статистика, най-нови обяви, намалени цени, популярни квартали |
| Страница „За проекта“ | `HomeController.About` — екип, основни функции, технологии, данни в момента |
| MVC архитектура | `Models/` (entities), `ViewModels/`, `Views/`, `Controllers/`, `Services/` |
| Навигационно меню | `Views/Shared/_Layout.cshtml` |
| Страници с динамично съдържание (≥ 3) | Обяви, Обява, Квартали, Квартал, Агенции, Агенция, Статистика, Любими, Моите обяви, Профил, Администрация |
| .NET 10 или по-нова | `net10.0` в `SuperImoti.Web.csproj` |
| Bootstrap | Bootstrap 5.3 във всички изгледи |
| Razor изгледи | `Views/**/*.cshtml` |
| Layout, Partial View, Section | `_Layout.cshtml`; partial-и `_ListingCard`, `_ListingGrid`, `_Pagination`, `_Filters`, `_ListingForm`, `_Gallery`, `_Comments`…; секции `Scripts`, `Hero`, `Styles` |
| SQL база чрез Entity Framework | SQLite + EF Core, `Data/ApplicationDbContext.cs`, миграции в `Data/Migrations` |
| Пълен CRUD (≥ 1 обект) | **Обяви** — `ListingsController` (Create, Details, Edit, Delete); **Квартали** — `DistrictsController` (за администратор) |
| Валидация в модела | DataAnnotations и `IValidatableObject` в `ViewModels/FormModels.cs`, `ImageFilesAttribute` за снимките |
| Валидация в изгледите | `asp-validation-for`, `asp-validation-summary` и jQuery Validation в браузъра |
| Обработени грешки | Страници за 404/403/400 (`UseStatusCodePagesWithReExecute`), `UseExceptionHandler`, проверка за едновременна редакция (`ConcurrencyStamp`), `try/catch` при запис, невалидни филтри се показват като предупреждение, липсващи снимки показват заместител |
| **Бонус:** връзки 1:N и M:N | Квартал → Обяви, Агенция → Обяви, Обява → Снимки/История/Коментари (1:N); Обява ↔ Особености и Потребител ↔ Любими обяви (M:N) |
| **Бонус:** търсене и филтриране | `ListingQueryService.Filter` |
| **Бонус:** регистрация и вход | ASP.NET Core Identity, `AccountController` |
| **Бонус:** JavaScript фреймуърк | Vue.js 3 — `Views/Stats/Index.cshtml`, `wwwroot/js/stats.js` |
| **Бонус:** пагинация и сортиране | `PagedResult`, `_Pagination`, `ListingSort` |
| **Бонус:** коментари | `CommentsController`, `_Comments.cshtml` |
| **Бонус:** качване и управление на снимки | Добавяне и премахване на снимки при публикуване/редакция (`PhotoStorage`) |
| **Бонус:** експорт на данни | CSV експорт на филтрираните обяви (`ListingsController.Export`) |
| **Бонус:** имейл известия | `NewListingNotifier` + фонова опашка `EmailBackgroundService` |

## Данните от скрейпъра

Импортът (`Data/Import/ScraperImporter.cs`) чете базата `imot_imot.db` само за четене:

| Таблица на скрейпъра | Става |
|---|---|
| `listings` | `Listings`, `Districts`, `PropertyTypes`, `Features`, `Sellers` |
| `listing_images` (само изтеглените, `content_hash IS NOT NULL`) | `ListingPhotos` |
| `listing_history` | `ListingEvents` (история на цената и статуса) |
| `listing_duplicates` | `Listings.DuplicateGroupId` (секция „Същият имот в други обяви“) |

Снимките не се копират: сайтът ги показва директно от папката `images/` на скрейпъра под адрес `/photos/…`,
като пътят се изчислява от хеша (`images/<hash[0:2]>/<hash[2:4]>/<hash>.jpg`), както е описано в README-то на скрейпъра.

## Структура

```
SuperImoti.Web/
  Controllers/      MVC контролери
  Models/           EF Core entities
  ViewModels/       модели за изгледите и формите (с валидация)
  Views/            Razor изгледи, Layout и partial-и
  Data/             DbContext, миграции, начални данни, импорт от скрейпъра
  Services/         заявки за обяви, снимки, CSV, имейли
  Infrastructure/   помощни класове (форматиране, страниране, валидация)
  wwwroot/          CSS, JavaScript, Bootstrap
SuperImoti.Tests/   xUnit тестове
```

## Тестове

```bash
dotnet test
```
