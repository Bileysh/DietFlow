"""
Генерує Postman-колекцію DietFlow.postman_collection.json (формат v2.1).
Запуск: python tests/postman/build_collection.py
Колекцію можна імпортувати в Postman або запустити з консолі: npx newman run ...
"""
import json, os

def req(name, method, path, tests, body=None, prerequest=None, headers=None, auth=None):
    item = {
        "name": name,
        "request": {
            "method": method,
            "header": [{"key": "Content-Type", "value": "application/json"}] if body is not None else [],
            "url": "{{baseUrl}}" + path,
        },
        "event": [{"listen": "test", "script": {"type": "text/javascript", "exec": tests.strip().splitlines()}}],
    }
    if auth is not None:
        item["request"]["auth"] = auth
    elif method == "GET" and not path.startswith("/api/v1/meals"):
        item["request"]["auth"] = {"type": "noauth"}
    if headers:
        item["request"]["header"] += [{"key": k, "value": v} for k, v in headers.items()]
    if body is not None:
        raw = json.dumps(body, ensure_ascii=False, indent=2)
        for var in ("cityId", "productId"):
            raw = raw.replace(f'"{{{{{var}}}}}"', f"{{{{{var}}}}}")
        item["request"]["body"] = {"mode": "raw", "raw": raw}
    if prerequest:
        item["event"].append({"listen": "prerequest", "script": {"type": "text/javascript", "exec": prerequest.strip().splitlines()}})
    return item

STATUS = lambda code: f'pm.test("Status {code}", () => pm.response.to.have.status({code}));'
PROBLEM = 'pm.test("ProblemDetails format", () => { const j = pm.response.json(); pm.expect(j).to.have.property("title"); pm.expect(j).to.have.property("status"); });'
PAGED = '''pm.test("Paged shape", () => {
    const j = pm.response.json();
    pm.expect(j).to.have.all.keys("items", "total", "skip", "limit", "nextLink");
    pm.expect(j.items.length).to.be.at.most(j.limit);
});'''

NOAUTH = {"type": "noauth"}
BEARER = lambda token: {"type": "bearer", "bearer": [{"key": "token", "value": token, "type": "string"}]}

ts = "{{$timestamp}}"
product = {"name": "Postman test product " + ts, "category": "Інше", "kcalPer100g": 250, "proteinPer100g": 10,
           "fatPer100g": 5, "carbsPer100g": 40, "barcode": "PM" + ts}

cities = [
    req("List cities", "GET", "/api/v1/cities", STATUS(200) + '''
const cities = pm.response.json();
pm.test("Non-empty array", () => pm.expect(cities).to.be.an("array").that.is.not.empty);
const kyiv = cities.find(c => c.name === "Київ") || cities[0];
pm.collectionVariables.set("cityId", kyiv.id);'''),
]

products = [
    req("List products (page 1)", "GET", "/api/v1/products?limit=20&cityId={{cityId}}", STATUS(200) + PAGED + '''
const j = pm.response.json();
pm.test("nextLink present when more items exist", () => {
    if (j.total > j.limit) pm.expect(j.nextLink).to.include("skip=20");
});
pm.collectionVariables.set("nextLink", j.nextLink || "");'''),
    req("Follow nextLink (page 2)", "GET", "/api/v1/products?skip=20&limit=20&cityId={{cityId}}", STATUS(200) + PAGED + '''
pm.test("Page 2 starts at skip=20", () => pm.expect(pm.response.json().skip).to.eql(20));''',
        prerequest='''
const next = pm.collectionVariables.get("nextLink");
if (next) pm.request.url = next;'''),
    req("Search products (q=кур)", "GET", "/api/v1/products?q=%D0%BA%D1%83%D1%80", STATUS(200) + '''
pm.test("All names contain 'кур'", () => pm.response.json().items.forEach(p => pm.expect(p.name.toLowerCase()).to.include("кур")));'''),
    req("Limit is clamped to 100", "GET", "/api/v1/products?limit=1000", STATUS(200) + '''
pm.test("limit <= 100", () => pm.expect(pm.response.json().limit).to.eql(100));'''),
    req("Get unknown product -> 404", "GET", "/api/v1/products/2147483647", STATUS(404) + PROBLEM),
    req("Create invalid product -> 400", "POST", "/api/v1/products",
        STATUS(400) + '''
pm.test("Validation errors listed", () => {
    const e = pm.response.json().errors;
    pm.expect(e).to.have.property("Name");
    pm.expect(e).to.have.property("KcalPer100g");
});''', body={"name": "", "kcalPer100g": 5000}),
    req("Create product -> 201", "POST", "/api/v1/products", STATUS(201) + '''
pm.test("Location header points to the new product", () => pm.expect(pm.response.headers.get("Location")).to.match(/\\/api\\/v1\\/products\\/\\d+$/));
const p = pm.response.json();
pm.test("Body echoes the product", () => pm.expect(p.kcalPer100g).to.eql(250));
pm.collectionVariables.set("productId", p.id);
pm.collectionVariables.set("barcode", p.barcode);''', body=product),
    req("Create duplicate barcode -> 409", "POST", "/api/v1/products", STATUS(409) + PROBLEM,
        body={**product, "name": "Duplicate", "barcode": "{{barcode}}"}),
    req("Get created product", "GET", "/api/v1/products/{{productId}}", STATUS(200) + '''
pm.test("Details shape", () => pm.expect(pm.response.json()).to.have.all.keys("product", "prices", "usedInRecipes"));'''),
    req("Cache: warm up", "GET", "/api/v1/products/{{productId}}?cityId={{cityId}}", STATUS(200)),
    req("Cache: second GET served from cache", "GET", "/api/v1/products/{{productId}}?cityId={{cityId}}", STATUS(200) + '''
pm.test("Age header present (Output Cache hit)", () => pm.expect(pm.response.headers.has("Age")).to.be.true);''',
        prerequest="setTimeout(() => {}, 1100);"),
    req("Update product -> 200", "PUT", "/api/v1/products/{{productId}}", STATUS(200) + '''
pm.test("Updated value returned", () => pm.expect(pm.response.json().kcalPer100g).to.eql(300));''',
        body={**product, "kcalPer100g": 300, "barcode": "{{barcode}}"}),
    req("Cache invalidated after update", "GET", "/api/v1/products/{{productId}}?cityId={{cityId}}", STATUS(200) + '''
pm.test("Fresh response (no Age)", () => pm.expect(pm.response.headers.has("Age")).to.be.false);
pm.test("New value visible immediately", () => pm.expect(pm.response.json().product.kcalPer100g).to.eql(300));'''),
]

stores = [
    req("List stores in city", "GET", "/api/v1/stores?cityId={{cityId}}", STATUS(200) + PAGED + '''
pm.test("All stores in requested city", () => pm.response.json().items.forEach(s => pm.expect(s.cityId).to.eql(+pm.collectionVariables.get("cityId"))));'''),
    req("Create store -> 201", "POST", "/api/v1/stores", STATUS(201) + '''
pm.collectionVariables.set("storeId", pm.response.json().id);''',
        body={"name": "Postman store " + ts, "chain": "АТБ", "address": "test", "latitude": 50.45, "longitude": 30.52, "cityId": "{{cityId}}"}),
    req("Set new price -> 201", "PUT", "/api/v1/stores/{{storeId}}/prices/{{productId}}", STATUS(201), body={"pricePerKg": 99.9}),
    req("Update price -> 200", "PUT", "/api/v1/stores/{{storeId}}/prices/{{productId}}", STATUS(200) + '''
pm.test("Price updated", () => pm.expect(pm.response.json().pricePerKg).to.eql(88.8));''', body={"pricePerKg": 88.8}),
    req("Invalid price -> 400", "PUT", "/api/v1/stores/{{storeId}}/prices/{{productId}}", STATUS(400), body={"pricePerKg": 0}),
    req("Store prices list", "GET", "/api/v1/stores/{{storeId}}/prices", STATUS(200) + PAGED + '''
pm.test("Contains our product", () => pm.expect(pm.response.json().items.map(i => i.productId)).to.include(+pm.collectionVariables.get("productId")));'''),
]

recipes = [
    req("List recipes sorted by cost", "GET", "/api/v1/recipes?cityId={{cityId}}&sort=cost&limit=100", STATUS(200) + PAGED + '''
const costs = pm.response.json().items.map(r => r.perServing && r.perServing.cost).filter(c => c != null);
pm.test("Costs ascending", () => costs.forEach((c, i) => { if (i) pm.expect(c).to.be.at.least(costs[i - 1]); }));
pm.test("Method serialized as string", () => pm.expect(pm.response.json().items[0].method).to.be.a("string"));'''),
    req("Create recipe -> 201", "POST", "/api/v1/recipes", STATUS(201) + '''
const r = pm.response.json();
pm.collectionVariables.set("recipeId", r.recipe.id);
pm.test("Slug generated (latin)", () => pm.expect(r.recipe.slug).to.match(/^[a-z0-9-]+$/));
pm.test("Kcal per serving computed: 200 g x 300 kcal/100 g / 2 servings = 300", () => pm.expect(r.recipe.perServing.kcal).to.eql(300));''',
        body={"title": "Тестовий рецепт Postman " + ts, "method": "Baked", "cookTimeMinutes": 15, "servings": 2,
              "ingredients": [{"productId": "{{productId}}", "grams": 200}]}),
    req("Recipe with unknown product -> 400", "POST", "/api/v1/recipes", STATUS(400),
        body={"title": "Bad", "method": "Raw", "ingredients": [{"productId": 2147483647, "grams": 100}]}),
    req("Delete product used in recipe -> 409", "DELETE", "/api/v1/products/{{productId}}", STATUS(409) + PROBLEM + '''
pm.test("Conflict explains which recipes", () => pm.expect(pm.response.json().detail).to.include("Postman"));'''),
    req("Recipe cost uses store price", "GET", "/api/v1/recipes/{{recipeId}}?cityId={{cityId}}", STATUS(200) + '''
const line = pm.response.json().ingredients[0];
pm.test("Ingredient has cheapest price", () => pm.expect(line.pricePerKg).to.not.be.null);'''),
    req("Delete recipe -> 204", "DELETE", "/api/v1/recipes/{{recipeId}}", STATUS(204)),
]

meals = [
    req("Create meal -> 201", "POST", "/api/v1/meals", STATUS(201) + '''
const m = pm.response.json();
pm.collectionVariables.set("mealId", m.id);
pm.test("Not analyzed yet", () => pm.expect(m.analyzed).to.be.false);''',
        body={"type": "Snack", "dishName": "Postman snack", "kcal": 150}),
    req("List meals", "GET", "/api/v1/meals?limit=5", STATUS(200) + PAGED),
    req("Analyze without photo -> 422/503", "POST", "/api/v1/meals/{{mealId}}/analyze", '''
pm.test("422 (no photo) or 503 (AI not configured)", () => pm.expect(pm.response.code).to.be.oneOf([422, 503]));''' + PROBLEM),
    req("Delete meal -> 204", "DELETE", "/api/v1/meals/{{mealId}}", STATUS(204)),
    req("Deleted meal -> 404", "GET", "/api/v1/meals/{{mealId}}", STATUS(404)),
]

auth = [
    req("No token -> 401", "GET", "/api/v1/meals", STATUS(401), auth=NOAUTH),
    req("Invalid token -> 401", "GET", "/api/v1/meals", STATUS(401), auth=BEARER("df_invalid-token")),
    req("Anonymous write -> 401", "POST", "/api/v1/products", STATUS(401), body={"name": "x"}, auth=NOAUTH),
    req("Valid token -> 200", "GET", "/api/v1/meals?limit=1", STATUS(200) + PAGED),
]

cleanup = [
    req("Delete product -> 204", "DELETE", "/api/v1/products/{{productId}}", STATUS(204)),
    req("Delete store -> 204", "DELETE", "/api/v1/stores/{{storeId}}", STATUS(204)),
]

collection = {
    "info": {
        "name": "DietFlow API v1",
        "description": "Автотести REST API DietFlow (QA1): CRUD, пагінація (B1), кешування (B9), коди помилок, авторизація (C16). "
                       "Запис і щоденник потребують персонального API-токена адміністратора: змінна apiToken.",
        "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
    },
    "auth": BEARER("{{apiToken}}"),
    "variable": [{"key": k, "value": ""} for k in ["cityId", "nextLink", "productId", "barcode", "storeId", "recipeId", "mealId"]],
    "item": [
        {"name": "Auth", "item": auth},
        {"name": "Cities", "item": cities},
        {"name": "Products", "item": products},
        {"name": "Stores & prices", "item": stores},
        {"name": "Recipes", "item": recipes},
        {"name": "Meals", "item": meals},
        {"name": "Cleanup", "item": cleanup},
    ],
}

here = os.path.dirname(os.path.abspath(__file__))
with open(os.path.join(here, "DietFlow.postman_collection.json"), "w", encoding="utf-8") as f:
    json.dump(collection, f, ensure_ascii=False, indent=2)
with open(os.path.join(here, "local.postman_environment.json"), "w", encoding="utf-8") as f:
    json.dump({"name": "DietFlow local", "values": [
        {"key": "baseUrl", "value": "http://localhost:5213", "enabled": True},
        {"key": "apiToken", "value": "", "type": "secret", "enabled": True},
    ]},
              f, ensure_ascii=False, indent=2)
print("requests:", sum(len(g["item"]) for g in collection["item"]))
