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
