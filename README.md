# DietFlow

Вебзастосунок для контролю харчування: фотографуєте страву → AI оцінює калорії, БЖВ і корисність →
якщо страва «погана», підказує, як її покращити, пропонує кращі рецепти з бази, рахує їхню вартість
і показує, у якому магазині інгредієнти найдешевші.

Проєкт з дисципліни «Web-технології».

## Можливості
- **AI-аналіз фото страви** — назва, інгредієнти, ккал, БЖВ, оцінка корисності 1–10, проблеми й поради, кращі альтернативи з каталогу рецептів.
  Провайдер обирається в конфігурації: **Google Gemini** (безкоштовний free tier, за замовчуванням) або **Claude**.
- **Камера та стиснення фото в браузері** — зйомка прямо на сайті, drag & drop, зменшення до 1280 px і видалення EXIF/GPS до відправки.
- **Щоденник харчування** — прийоми їжі за днями, прогрес до денної норми, графік калорій і корисності за тиждень.
- **Рецепти** — розрахунок калорій, БЖВ і вартості порції за найнижчими цінами в обраному місті; landing-сторінка кожного рецепта з SEO-розміткою.
- **Продукти й магазини** — ~2,4 млн продуктів (Open Food Facts) з миттєвим autocomplete і цінами в магазинах.
- **Реальні магазини з OpenStreetMap** — ~3 900 супермаркетів 19 мереж у ~500 містах України; місто визначається автоматично (кнопка 📍).
- **Мапа магазинів** — Leaflet + OpenStreetMap: режими «Місто» і «Поруч зі мною» (радіус від геолокації), фільтри за мережами й продуктом (маркери з ціною), найближчий і найдешевший магазин.
- **Ціни на основі відкритих даних Держстату** — середні споживчі ціни по областях (офіційний SDMX API, CC BY 4.0) для 20 базових продуктів; ціни в магазинах — оцінка «ціна області × коефіцієнт мережі».
- **Ціни з позначкою джерела** — «📊 Держстат» (оцінка), «демо» (згенеровані) або «✓ перевірено» (введені адміністратором вручну, мають пріоритет і не перезаписуються).
- **Вхід через Google** (OAuth2, ASP.NET Identity): кожен бачить лише свій щоденник; каталог (продукти, магазини, рецепти, ціни) змінює лише адміністратор.
- **Плани Free / Premium** через feature flags: Free — 5 AI-аналізів на день, Premium — без ліміту і **тижневий план харчування** (7 днів × 3 прийоми під норму калорій, найдешевші рецепти, список покупок).
- **REST API v1** з пагінацією, кешуванням, персональними API-токенами та автотестами Postman і xUnit.

## Стек
- ASP.NET Core 10 (MVC + Web API), Entity Framework Core 10, PostgreSQL 17 (`pg_trgm`)
- Bootstrap 5, jQuery, Select2, Chart.js, Leaflet + OpenStreetMap
- Google Gemini API / Claude API (офіційний SDK `Anthropic`)
- Docker / Docker Compose, Azure for Students (план)
- Postman + Newman, Scalar (документація API)

## Запуск локально

**Потрібно:** .NET 10 SDK, Docker Desktop (запущений).

```bash
cp .env.example .env               # пароль для PostgreSQL у Docker
docker-compose up -d               # PostgreSQL на хост-порту 5433
```

Рядок підключення й ключі зберігаються в `dotnet user-secrets` (не в git):
```bash
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5433;Database=dietflow;Username=dietflow;Password=<пароль з .env>" --project src/DietFlow.Web
dotnet user-secrets set "Gemini:ApiKey" "<ключ з aistudio.google.com>" --project src/DietFlow.Web
dotnet user-secrets set "Authentication:Google:ClientId" "<Client ID>" --project src/DietFlow.Web
dotnet user-secrets set "Authentication:Google:ClientSecret" "<Client secret>" --project src/DietFlow.Web
dotnet user-secrets set "Admin:Emails:0" "<ваш gmail>" --project src/DietFlow.Web
```

**Вхід через Google:** у [Google Cloud Console](https://console.cloud.google.com/) створіть OAuth client ID типу *Web application*
з Authorized redirect URIs `http://localhost:5213/signin-google` і `https://localhost:7292/signin-google`.
Без ключів Google сайт працює: у режимі розробки на сторінці входу є кнопки «Демо-адмін» і «Демо-користувач».

```bash
dotnet run --project src/DietFlow.Web    # http://localhost:5213
```
При першому запуску (Development) автоматично застосовуються міграції і заповнюються демо-дані:
5 міст, 27 магазинів, 49 продуктів з цінами, 30 рецептів з фото, два демо-користувачі (адмін і звичайний) і кілька записів щоденника.

> Порт **5433**, а не 5432 — щоб не конфліктувати з локально встановленим PostgreSQL.
> Якщо команда `docker compose` не розпізнається, використовуйте `docker-compose`.
> Увесь стек у Docker: `docker-compose --profile full up --build` → http://localhost:8080

### Магазини (OpenStreetMap)
Реальні супермаркети продуктових мереж України завантажуються з [OpenStreetMap](https://www.openstreetmap.org) (ODbL) через Overpass API;
міста створюються автоматично, магазини оновлюються за ідентифікатором OSM, демо-точки сідера замінюються. Кожне місто прив'язується до області (для регіональних цін Держстату);
новим магазинам генеруються ціни — за Держстатом, якщо його вже імпортовано, інакше демо.
```bash
dotnet run --project src/DietFlow.Web -- import-osm-stores
```
Відповіді Overpass кешуються локально на 7 днів; `--refresh` завантажує заново.

### Ціни (Держстат)
Середні споживчі ціни по областях беруться з [офіційного SDMX API Держстату](https://stat.gov.ua/uk/development-api)
(показник `AVG_CONS_PRCS`, ліцензія CC BY 4.0, оновлюється щомісяця). Товари Держстату зіставляються з продуктами каталогу,
ціни перераховуються в грн/кг, а ціни магазинів (крім введених адміністратором вручну) — як «ціна області × коефіцієнт мережі ± 4%».
Це оцінка: відкритих даних про ціни конкретних магазинів в Україні немає.
```bash
dotnet run --project src/DietFlow.Web -- import-stat-prices
```

### Довідник продуктів (Open Food Facts)
Для autocomplete імпортується відкрита база [Open Food Facts](https://world.openfoodfacts.org) (ліцензія ODbL) — ~2,4 млн продуктів з калоріями та БЖВ.
Файл (~1,1 ГБ) читається потоково, на диск не зберігається; запис через PostgreSQL `COPY`; повторний запуск додає лише нові продукти (за штрихкодом).
```bash
dotnet run --project src/DietFlow.Web -- import-off                 # усе, ~5 хв
dotnet run --project src/DietFlow.Web -- import-off --limit 50000   # частина
```

## Конфігурація
| Ключ | Призначення | За замовчуванням |
|---|---|---|
| `ConnectionStrings:Default` | PostgreSQL | — (user-secrets) |
| `Ai:Provider` | `Gemini` або `Claude` | `Gemini` |
| `Gemini:ApiKey`, `Gemini:Model` | Google Gemini | —, `gemini-flash-latest` |
| `Anthropic:ApiKey`, `Anthropic:Model` | Claude | —, `claude-haiku-5-5` |
| `Gemini:FallbackModel` | запасна модель, якщо основна перевантажена | `gemini-flash-lite-latest` |
| `Authentication:Google:ClientId`, `…:ClientSecret` | вхід через Google | — (user-secrets) |
| `Admin:Emails` | email-и адміністраторів (роль Admin) | — (user-secrets) |
| `Plans:FreeDailyAnalyses` | ліміт AI-аналізів на день для Free | `5` |
| `FeatureManagement` | feature flags `UnlimitedAiAnalysis`, `WeeklyMealPlan` (увімкнені для групи Premium) | див. appsettings.json |
| `Site:ContactEmail`, `Site:ContactPhone`, `Site:TelegramUrl` | контакти на landing-сторінках | демо-значення |

Без ключа AI застосунок працює повністю, лише кнопка AI-аналізу неактивна (поля можна заповнити вручну).

## Сторінки
| Адреса | Що там |
|---|---|
| `/` | головна |
| `/Meals` | щоденник: прийоми їжі, AI-аналіз фото, графік за тиждень |
| `/Recipes` | каталог рецептів з фільтрами й сортуванням за ціною/калоріями |
| `/recipes/{slug}` | landing-сторінка рецепта (напр. `/recipes/hrechka-z-kurkoiu-i-brokoli`) |
| `/Products`, `/Stores` | довідники продуктів, магазинів і цін; перемикач міста в шапці |
| `/Map` | мапа магазинів: місто або «поруч зі мною», фільтри (`/Map?productId=` — одразу з цінами продукту) |
| `/Account/Login` | вхід через Google (у Development — ще й демо-вхід) |
| `/Profile` | денна норма, план Free/Premium, персональні API-токени |
| `/Premium` | порівняння планів |
| `/MealPlan` | тижневий план харчування (Premium) |
| `/scalar` | інтерактивна документація REST API |
| `/sitemap.xml`, `/robots.txt` | для пошукових систем |

## REST API (v1)
Документація: **http://localhost:5213/scalar** · OpenAPI: `/openapi/v1.json`

| Ресурс | Ендпоінти |
|---|---|
| Міста | `GET /api/v1/cities` |
| Продукти | `GET/POST /api/v1/products`, `GET/PUT/DELETE /api/v1/products/{id}`, `GET /api/v1/products/search?term=` (autocomplete) |
| Магазини, ціни | `GET /api/v1/stores/nearby?lat=&lng=&radiusKm=`, `GET/POST /api/v1/stores`, `GET/PUT/DELETE /api/v1/stores/{id}`, `GET /api/v1/stores/{id}/prices`, `PUT/DELETE /api/v1/stores/{id}/prices/{productId}` |
| Рецепти | `GET/POST /api/v1/recipes`, `GET/PUT/DELETE /api/v1/recipes/{id}` (калорії й вартість розраховуються, `?cityId=`) |
| Щоденник | `GET/POST /api/v1/meals`, `GET/PUT/DELETE /api/v1/meals/{id}`, `POST /api/v1/meals/{id}/photo`, `POST /api/v1/meals/{id}/analyze` |

- **Авторизація:** читання каталогу — відкрите; щоденник і зміни — з персональним токеном із `/Profile`:
  `Authorization: Bearer df_...` (у базі зберігається лише SHA-256 хеш токена). Без токена — `401`, без ролі адміністратора — `403`,
  чужий прийом їжі — `404`, вичерпаний ліміт AI на Free — `429`.
- **Пагінація:** `?skip=0&limit=20` (limit ≤ 100), відповідь `{ items, total, skip, limit, nextLink }`.
- **Помилки:** ProblemDetails (RFC 9457) — `400` валідація, `404`, `409` конфлікт, `422`/`503` для AI-аналізу.
- **Кешування:** GET-відповіді кешуються на 60 с (заголовок `Age`), кеш скидається одразу після змін через API або сайт.

```bash
curl "http://localhost:5213/api/v1/recipes?cityId=1&sort=cost&limit=5"
```

## Тести
**xUnit + WebApplicationFactory** (`tests/DietFlow.Tests`) — 24 тести автентифікації й авторизації: редирект на вхід,
401/403/404, дійсний/недійсний/відкликаний токен, ліміт Free і безліміт Premium, доступ до тижневого плану.
Застосунок запускається в пам'яті проти окремої бази `dietflow_test` (створюється з нуля), AI і сховище фото підмінені:
```bash
dotnet test
```

**Postman-колекція** `tests/postman/DietFlow.postman_collection.json` — 37 запитів, 71 перевірка
(CRUD, пагінація, кеш, коди помилок, авторизація). Запис потребує токена адміністратора: увійдіть як «Демо-адмін»,
створіть токен у `/Profile` і передайте його (токен не зберігається в репозиторії):
```bash
npx newman run tests/postman/DietFlow.postman_collection.json -e tests/postman/local.postman_environment.json --env-var apiToken=df_...
```
Колекція генерується скриптом `tests/postman/build_collection.py`. Можна також імпортувати обидва файли в Postman і запустити **Run collection**.

## Модель даних
| Сутність | Призначення |
|---|---|
| AppUser | користувач (ASP.NET Identity), денна норма ккал, план Free/Premium; вхід через Google, роль Admin |
| ApiToken | персональний токен REST API: назва, SHA-256 хеш, час створення/використання/відкликання |
| AiAnalysisLog | журнал AI-аналізів — для денного ліміту плану Free |
| Meal | прийом їжі: **фото (файл)**, результат AI-аналізу (`jsonb`) |
| MealRecommendation | Meal ↔ Recipe: що AI порадив замість страви і чому |
| Recipe | рецепт: slug, кроки (Markdown), спосіб приготування, час, порції, фото |
| RecipeIngredient | Recipe ↔ Product, грами |
| Product | продукт: ккал/БЖВ на 100 г, категорія, штрихкод, пріоритет сортування (згенерована колонка) |
| City | місто з координатами центру (створюються автоматично при імпорті OSM) |
| OfficialPrice | середні ціни Держстату по областях (код КАТОТТГ) за місяць; City.RegionCode прив'язує місто до області |
| Store / StorePrice | магазини (мережа, координати, посилання на OSM) і ціни продуктів за кг із джерелом (демо / Держстат / вручну) |

Калорії й вартість рецептів не зберігаються, а рахуються з інгредієнтів і поточних цін — зміна ціни одразу відображається в усіх рецептах.

## Структура
```
src/DietFlow.Web/
  Controllers/        MVC-контролери (сторінки)
  Controllers/Api/    REST API v1
  Data/               DbContext, сидер, імпорт Open Food Facts
  Models/             сутності, ViewModels, DTO API
  Services/           розрахунки, правила, кеш, файли, AI (Services/Ai), вхід і токени (Services/Auth), тижневий план
  Views/              Razor-сторінки
  wwwroot/            JS (камера, autocomplete, графіки), CSS, фото рецептів
tests/DietFlow.Tests/ xUnit-тести автентифікації й авторизації (WebApplicationFactory)
tests/postman/        колекція й оточення Postman
```

## Виконані завдання
| Код | Опис | Статус |
|---|---|---|
| C1 | MVC: 8 сутностей зі зв'язками, поле-файл (фото), CRUD | ✅ |
| C15 | Низькорівневі API браузера: камера (getUserMedia), File API, Canvas-стиснення | ✅ |
| C5 | REST API з CRUD і кодами 200/201/204/4xx/5xx | ✅ |
| B1 | Пагінація skip/limit/nextLink | ✅ |
| B9 | Кешування відповідей API (Output Cache + інвалідація за тегами) | ✅ |
| QA1 | Автотести API в Postman/Newman | ✅ |
| F2 | Autocomplete (Select2 + AJAX, від 3 символів, debounce) на ~2,4 млн продуктів | ✅ |
| C6 | Landing-сторінка рецепта (Bootstrap, контакти постійно на екрані) | ✅ |
| C7 | SEO: meta, Open Graph, schema.org/Recipe, sitemap, robots | ✅ (аудит Seobility — після деплою) |
| F1 | Графіки Chart.js (щоденник, БЖВ рецепта) | ✅ |
| C8 | Мапа ~3 900 реальних магазинів (Leaflet/OSM): режим «поруч», фільтри мереж і цін, найближчий магазин | ✅ |
| C4 | Docker + Docker Compose | 🟡 перевірка повного стеку — разом із деплоєм |
| C16 | Вхід через Google (OAuth2, ASP.NET Identity), роль Admin, персональні API-токени | ✅ |
| B6 | Free / Premium через feature flags (Microsoft.FeatureManagement): ліміт AI, тижневий план | ✅ |
| QA3 | Тести автентифікації й авторизації (xUnit + WebApplicationFactory) | ✅ |
| C21, C22, B2, C12, F3, QA2, C3, C2, C9 | — | ⏳ заплановано |

Джерела фото й даних — у [CREDITS.md](CREDITS.md).
