# DietFlow

Вебзастосунок для контролю харчування: фотографуєте страву → AI (Claude) оцінює калорії, БЖВ і корисність →
якщо страва «погана», пропонує кращі рецепти з бази, рахує їхню вартість і показує, де інгредієнти найдешевші.

Проєкт з дисципліни «Web-технології».

## Стек
- ASP.NET Core 10 MVC + Web API
- Entity Framework Core 10 + PostgreSQL 17
- Docker / Docker Compose
- Azure for Students (App Service + Azure Database for PostgreSQL)
- Claude API (аналіз фото, AI-агент)

## Локальний запуск
```bash
cp .env.example .env            # за потреби змініть пароль (тоді й у appsettings.Development.json)
docker compose up -d            # піднімає лише PostgreSQL (на хост-порту 5433)
dotnet run --project src/DietFlow.Web
```
Увесь стек у Docker: `docker compose --profile full up --build` → http://localhost:8080

> Якщо `docker compose` не розпізнається, використовуйте окрему утиліту `docker-compose` з тими самими аргументами.

## REST API (v1)
Інтерактивна документація: **http://localhost:5213/scalar** · OpenAPI: `/openapi/v1.json`

| Ресурс | Ендпоінти |
|---|---|
| Міста | `GET /api/v1/cities` |
| Продукти | `GET/POST /api/v1/products`, `GET/PUT/DELETE /api/v1/products/{id}` |
| Магазини, ціни | `GET/POST /api/v1/stores`, `GET/PUT/DELETE /api/v1/stores/{id}`, `GET /api/v1/stores/{id}/prices`, `PUT/DELETE /api/v1/stores/{id}/prices/{productId}` |
| Рецепти | `GET/POST /api/v1/recipes`, `GET/PUT/DELETE /api/v1/recipes/{id}` (калорії й вартість розраховуються, `?cityId=`) |
| Щоденник | `GET/POST /api/v1/meals`, `GET/PUT/DELETE /api/v1/meals/{id}`, `POST /api/v1/meals/{id}/photo`, `POST /api/v1/meals/{id}/analyze` |

- **Пагінація:** `?skip=0&limit=20` (limit ≤ 100), відповідь `{ items, total, skip, limit, nextLink }`.
- **Помилки:** ProblemDetails (RFC 9457) — `400` валідація, `404`, `409` конфлікт, `422`/`503` для AI-аналізу.
- **Кешування:** GET-відповіді кешуються на 60 с (заголовок `Age`), кеш скидається одразу після змін через API або сайт.

```bash
curl "http://localhost:5213/api/v1/products?q=кур&limit=5&cityId=1"
```

### Автотести API (Postman / Newman)
Колекція: `tests/postman/DietFlow.postman_collection.json` (33 запити, 66 перевірок). Запуск при працюючому застосунку:
```bash
npx newman run tests/postman/DietFlow.postman_collection.json -e tests/postman/local.postman_environment.json
```

## Модель даних
| Сутність | Призначення |
|---|---|
| AppUser | користувач (ASP.NET Identity), денна норма ккал, план Free/Premium |
| Meal | прийом їжі: **фото (файл)**, результат AI-аналізу |
| Recipe | рецепт: кроки (Markdown), спосіб приготування, фото |
| Product | продукт: ккал/БЖВ на 100 г |
| RecipeIngredient | Recipe ↔ Product, грами |
| Store / StorePrice | магазини з координатами, ціни продуктів |
| MealRecommendation | Meal ↔ Recipe: що AI порадив замість страви |

## Виконані завдання
| Код | Опис | Статус |
|---|---|---|
| C4 | Docker + Docker Compose | 🟡 в роботі |
| C1 | MVC, 3+ сутності, файл, CRUD | 🟡 в роботі |
