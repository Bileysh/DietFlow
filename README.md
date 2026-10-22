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
- **Продукти й магазини** — ~2,4 млн продуктів (Open Food Facts) з миттєвим autocomplete, 5 міст, 5 мереж, ціни в магазинах.
- **REST API v1** з пагінацією, кешуванням і автотестами Postman.

## Стек
- ASP.NET Core 10 (MVC + Web API), Entity Framework Core 10, PostgreSQL 17 (`pg_trgm`)
- Bootstrap 5, jQuery, Select2, Chart.js
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
```

```bash
dotnet run --project src/DietFlow.Web    # http://localhost:5213
```
При першому запуску (Development) автоматично застосовуються міграції і заповнюються демо-дані:
5 міст, 27 магазинів, 49 продуктів з цінами, 30 рецептів з фото, демо-користувач і кілька записів щоденника.

> Порт **5433**, а не 5432 — щоб не конфліктувати з локально встановленим PostgreSQL.
> Якщо команда `docker compose` не розпізнається, використовуйте `docker-compose`.
> Увесь стек у Docker: `docker-compose --profile full up --build` → http://localhost:8080

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
| `/scalar` | інтерактивна документація REST API |
| `/sitemap.xml`, `/robots.txt` | для пошукових систем |

## REST API (v1)
Документація: **http://localhost:5213/scalar** · OpenAPI: `/openapi/v1.json`

| Ресурс | Ендпоінти |
|---|---|
| Міста | `GET /api/v1/cities` |
| Продукти | `GET/POST /api/v1/products`, `GET/PUT/DELETE /api/v1/products/{id}`, `GET /api/v1/products/search?term=` (autocomplete) |
| Магазини, ціни | `GET/POST /api/v1/stores`, `GET/PUT/DELETE /api/v1/stores/{id}`, `GET /api/v1/stores/{id}/prices`, `PUT/DELETE /api/v1/stores/{id}/prices/{productId}` |
| Рецепти | `GET/POST /api/v1/recipes`, `GET/PUT/DELETE /api/v1/recipes/{id}` (калорії й вартість розраховуються, `?cityId=`) |
| Щоденник | `GET/POST /api/v1/meals`, `GET/PUT/DELETE /api/v1/meals/{id}`, `POST /api/v1/meals/{id}/photo`, `POST /api/v1/meals/{id}/analyze` |

- **Пагінація:** `?skip=0&limit=20` (limit ≤ 100), відповідь `{ items, total, skip, limit, nextLink }`.
- **Помилки:** ProblemDetails (RFC 9457) — `400` валідація, `404`, `409` конфлікт, `422`/`503` для AI-аналізу.
- **Кешування:** GET-відповіді кешуються на 60 с (заголовок `Age`), кеш скидається одразу після змін через API або сайт.

```bash
curl "http://localhost:5213/api/v1/recipes?cityId=1&sort=cost&limit=5"
```

## Тести
Postman-колекція `tests/postman/DietFlow.postman_collection.json` — 33 запити, 66 перевірок
(CRUD, пагінація, кеш, коди помилок). Запуск при працюючому застосунку:
```bash
npx newman run tests/postman/DietFlow.postman_collection.json -e tests/postman/local.postman_environment.json
```
Колекція генерується скриптом `tests/postman/build_collection.py`. Можна також імпортувати обидва файли в Postman і запустити **Run collection**.

## Модель даних
| Сутність | Призначення |
|---|---|
| AppUser | користувач (ASP.NET Identity), денна норма ккал, план Free/Premium |
| Meal | прийом їжі: **фото (файл)**, результат AI-аналізу (`jsonb`) |
| MealRecommendation | Meal ↔ Recipe: що AI порадив замість страви і чому |
| Recipe | рецепт: slug, кроки (Markdown), спосіб приготування, час, порції, фото |
| RecipeIngredient | Recipe ↔ Product, грами |
| Product | продукт: ккал/БЖВ на 100 г, категорія, штрихкод, пріоритет сортування (згенерована колонка) |
| City | місто з координатами центру |
| Store / StorePrice | магазини (мережа, координати) і ціни продуктів за кг |

Калорії й вартість рецептів не зберігаються, а рахуються з інгредієнтів і поточних цін — зміна ціни одразу відображається в усіх рецептах.

## Структура
```
src/DietFlow.Web/
  Controllers/        MVC-контролери (сторінки)
  Controllers/Api/    REST API v1
  Data/               DbContext, сидер, імпорт Open Food Facts
  Models/             сутності, ViewModels, DTO API
  Services/           розрахунки, правила, кеш, файли, AI (Services/Ai)
  Views/              Razor-сторінки
  wwwroot/            JS (камера, autocomplete, графіки), CSS, фото рецептів
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
| C4 | Docker + Docker Compose | 🟡 перевірка повного стеку — разом із деплоєм |
| C8, C16, B6, QA3, C21, C22, B2, C12, F3, QA2, C3, C2, C9 | — | ⏳ заплановано |

Джерела фото й даних — у [CREDITS.md](CREDITS.md).
