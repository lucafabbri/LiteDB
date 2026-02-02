# Query Strategies per LiteDB AOT

## ?? PROBLEMA ATTUALE

Il sistema di query attuale usa:
1. **LINQ Expressions** ? Convertite runtime in `BsonExpression`
2. **`Expression.Compile()`** ? NON supportato in AOT
3. **Reflection** ? Per type inspection durante conversione

```csharp
// ? Non funziona in AOT
var results = collection.Find(x => x.Age > 18 && x.Name.StartsWith("J"));
//                             ? LinqExpressionVisitor usa reflection
```

---

## ?? PROPOSTA: 4 APPROCCI COMPLEMENTARI

### Approccio 1: String-Based Queries (SQL-like) ??

**Come MongoDB/SQL** - Il più compatibile AOT ma meno type-safe.

```csharp
// ? AOT-compatible - String parsing non richiede reflection
public class AppDbContext : LiteDbContext
{
    public ILiteCollection<Customer> Customers => Collection<Customer>();
}

using var db = new AppDbContext();

// === ESEMPI BASE ===

// 1. Equality
var john = db.Customers.FindOne("Name = 'John'");
var adult = db.Customers.Find("Age > 18");

// 2. Parametri (per evitare injection)
var byName = db.Customers.Find("Name = @name", new { name = "John" });
var byAge = db.Customers.Find("Age > @minAge AND City = @city", 
    new { minAge = 18, city = "NYC" });

// 3. LIKE operator
var startsWith = db.Customers.Find("Name LIKE 'J%'");
var contains = db.Customers.Find("Email LIKE '%@gmail.com'");

// 4. IN operator
var cities = db.Customers.Find("City IN ('NYC', 'LA', 'Chicago')");
var ids = db.Customers.Find("_id IN @ids", new { ids = new[] { 1, 2, 3 } });

// 5. NULL checks
var noEmail = db.Customers.Find("Email IS NULL");
var hasAddress = db.Customers.Find("Address IS NOT NULL");

// 6. Complex conditions
var complex = db.Customers.Find(@"
    (Age > 18 AND City = 'NYC') OR 
    (VIP = true AND TotalOrders > 100)
");

// 7. Nested properties
var premium = db.Customers.Find("Subscription.Type = 'Premium'");
var highSpender = db.Customers.Find("Orders[*].Total > 1000");

// 8. OrderBy
var ordered = db.Customers.Query()
    .Where("Age > 18")
    .OrderBy("Name")
    .ThenByDescending("CreatedAt")
    .ToList();

// 9. Aggregation
var count = db.Customers.Count("City = 'NYC'");
var exists = db.Customers.Exists("Email = @email", new { email = "test@example.com" });

// 10. Select/Projection (limitato)
var names = db.Customers.Query()
    .Where("Age > 18")
    .Select("{ name: $.Name, email: $.Email }")
    .ToList();
```

**PRO**:
- ? 100% AOT-compatible
- ? Potente e flessibile
- ? Familiare a chi usa SQL/MongoDB
- ? Supporta tutte le features di BsonExpression

**CONTRO**:
- ? No compile-time type checking
- ? Errori solo a runtime
- ? Refactoring più difficile
- ? IntelliSense limitato

---

### Approccio 2: Fluent Query Builder ??

**Type-safe ma verboso** - Migliore developer experience.

```csharp
using var db = new AppDbContext();

// === QUERY BUILDER FLUENT API ===

// 1. Equality
var john = db.Customers.Query()
    .Where(c => c.Property("Name").Equals("John"))
    .FirstOrDefault();

// 2. Comparisons
var adults = db.Customers.Query()
    .Where(c => c.Property("Age").GreaterThan(18))
    .ToList();

var seniors = db.Customers.Query()
    .Where(c => c.Property("Age").Between(60, 100))
    .ToList();

// 3. String operations
var startsWithJ = db.Customers.Query()
    .Where(c => c.Property("Name").StartsWith("J"))
    .ToList();

var gmail = db.Customers.Query()
    .Where(c => c.Property("Email").Contains("@gmail.com"))
    .ToList();

// 4. Logical operators
var complexQuery = db.Customers.Query()
    .Where(c => c.Or(
        b => b.And(
            x => x.Property("Age").GreaterThan(18),
            x => x.Property("City").Equals("NYC")
        ),
        b => b.And(
            x => x.Property("VIP").Equals(true),
            x => x.Property("TotalOrders").GreaterThan(100)
        )
    ))
    .ToList();

// 5. IN operator (type-safe)
var citiesQuery = db.Customers.Query()
    .Where(c => c.Property("City").In("NYC", "LA", "Chicago"))
    .ToList();

// 6. NULL checks
var noEmail = db.Customers.Query()
    .Where(c => c.Property("Email").IsNull())
    .ToList();

// 7. Nested properties
var premium = db.Customers.Query()
    .Where(c => c.Nested("Subscription").Property("Type").Equals("Premium"))
    .ToList();

// 8. OrderBy chain
var ordered = db.Customers.Query()
    .Where(c => c.Property("Age").GreaterThan(18))
    .OrderBy(c => c.Property("Name"))
    .ThenByDescending(c => c.Property("CreatedAt"))
    .ToList();

// 9. Pagination
var page = db.Customers.Query()
    .Where(c => c.Property("Active").Equals(true))
    .OrderBy(c => c.Property("Name"))
    .Skip(20)
    .Limit(10)
    .ToList();

// 10. Count/Exists
var count = db.Customers.Query()
    .Where(c => c.Property("City").Equals("NYC"))
    .Count();

var exists = db.Customers.Query()
    .Where(c => c.Property("Email").Equals("test@test.com"))
    .Exists();
```

**Implementazione (sketch)**:

```csharp
public interface IQueryBuilder<T>
{
    IQueryBuilder<T> Where(Func<IPropertyBuilder<T>, IQueryCondition> predicate);
    IQueryBuilder<T> OrderBy(Func<IPropertyBuilder<T>, IPropertySelector> selector);
    IQueryBuilder<T> ThenBy(Func<IPropertyBuilder<T>, IPropertySelector> selector);
    IQueryBuilder<T> Skip(int count);
    IQueryBuilder<T> Limit(int count);
    List<T> ToList();
    T FirstOrDefault();
    int Count();
    bool Exists();
}

public interface IPropertyBuilder<T>
{
    IPropertySelector Property(string name);
    IPropertyBuilder<T> Nested(string path);
    IQueryCondition And(params Func<IPropertyBuilder<T>, IQueryCondition>[] conditions);
    IQueryCondition Or(params Func<IPropertyBuilder<T>, IQueryCondition>[] conditions);
}

public interface IPropertySelector
{
    IQueryCondition Equals(object value);
    IQueryCondition GreaterThan(object value);
    IQueryCondition LessThan(object value);
    IQueryCondition Between(object min, object max);
    IQueryCondition StartsWith(string value);
    IQueryCondition Contains(string value);
    IQueryCondition In(params object[] values);
    IQueryCondition IsNull();
    IQueryCondition IsNotNull();
}
```

**PRO**:
- ? AOT-compatible
- ? Type-safe property names (verificati dal mapper)
- ? IntelliSense completo
- ? Composabile e testabile

**CONTRO**:
- ? Verboso
- ? Curva di apprendimento
- ? Lambda expressions potrebbero confondere (ma sono solo per fluent API, non compilate)

---

### Approccio 3: Strongly-Typed Query Extensions ??

**Compromesso** - Type-safe per operazioni comuni.

```csharp
using var db = new AppDbContext();

// === STRONGLY TYPED EXTENSIONS ===

// 1. Property selector con Expression (solo per property name extraction)
var adults = db.Customers.Where(x => x.Age, op => op.GreaterThan(18));
//                               ? Expression analizzata SOLO per nome property
//                                              ? Builder type-safe

// 2. Multiple conditions
var query = db.Customers
    .Where(x => x.Age, op => op.Between(18, 65))
    .Where(x => x.City, op => op.Equals("NYC"))
    .OrderBy(x => x.Name)
    .ToList();

// 3. String operations
var startsWith = db.Customers
    .Where(x => x.Name, op => op.StartsWith("J"))
    .ToList();

// 4. Nested properties (type-safe)
var premium = db.Customers
    .Where(x => x.Subscription.Type, op => op.Equals("Premium"))
    .ToList();

// 5. Collections
var bigOrders = db.Customers
    .Where(x => x.Orders.Any(o => o.Total > 1000))
    .ToList();
```

**Implementazione**:

```csharp
public static class QueryExtensions
{
    // Estrae solo il NOME della property dall'expression, non compila
    public static IQueryBuilder<T> Where<T, TProp>(
        this IQueryBuilder<T> query,
        Expression<Func<T, TProp>> propertySelector,
        Func<IPropertyOperator<TProp>, IQueryCondition> condition)
    {
        // Source Generator può pre-analizzare questo pattern
        var propertyName = ExtractPropertyName(propertySelector);
        var op = new PropertyOperator<TProp>(propertyName);
        var cond = condition(op);
        return query.AddCondition(cond);
    }
    
    // NO Expression.Compile() - solo analisi statica dell'AST
    private static string ExtractPropertyName<T, TProp>(Expression<Func<T, TProp>> expr)
    {
        if (expr.Body is MemberExpression member)
        {
            return member.Member.Name;
        }
        throw new ArgumentException("Expression must be a property accessor");
    }
}

public interface IPropertyOperator<T>
{
    IQueryCondition Equals(T value);
    IQueryCondition GreaterThan(T value) where T : IComparable;
    IQueryCondition StartsWith(T value) where T : string;
    // etc.
}
```

**PRO**:
- ? AOT-compatible (expressions NON compilate)
- ? Type-safe su property names E values
- ? IntelliSense completo
- ? Sintassi familiare

**CONTRO**:
- ?? Richiede Source Generator per pre-analizzare expressions
- ?? Expressions complesse potrebbero non essere supportate

---

### Approccio 4: Pre-Compiled Queries (Advanced) ?

**Per query critiche** - Massima performance.

```csharp
public class AppDbContext : LiteDbContext
{
    public ILiteCollection<Customer> Customers => Collection<Customer>();
    
    // Queries pre-compilate - Source Generator le converte
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(x => x.Id);
            
            // Definizione query riutilizzabili
            entity.HasQuery("ActiveAdults", 
                builder => builder
                    .Where("Age > 18")
                    .Where("Active = true")
                    .OrderBy("Name"));
            
            entity.HasQuery("ByCity", 
                builder => builder
                    .Where("City = @city")
                    .OrderBy("Name"));
            
            entity.HasQuery("RecentOrders",
                builder => builder
                    .Where("Orders[*].CreatedAt > @since")
                    .OrderByDescending("Orders[*].CreatedAt"));
        });
    }
}

// Uso
using var db = new AppDbContext();

// Query pre-compilata con parametri
var adults = db.Customers.ExecuteQuery("ActiveAdults").ToList();

var nycCustomers = db.Customers
    .ExecuteQuery("ByCity", new { city = "NYC" })
    .ToList();

var recent = db.Customers
    .ExecuteQuery("RecentOrders", new { since = DateTime.Now.AddMonths(-1) })
    .ToList();
```

**Source Generator Output**:

```csharp
// Auto-generated
partial class AppDbContext
{
    private static class CompiledQueries
    {
        public static readonly BsonExpression ActiveAdults = 
            BsonExpression.Create("Age > 18 AND Active = true");
        
        public static readonly BsonExpression ByCity = 
            BsonExpression.Create("City = @city");
        
        public static readonly BsonExpression RecentOrders = 
            BsonExpression.Create("Orders[*].CreatedAt > @since");
    }
    
    public IEnumerable<Customer> ExecuteQuery(string queryName, object parameters = null)
    {
        var expr = queryName switch
        {
            "ActiveAdults" => CompiledQueries.ActiveAdults,
            "ByCity" => CompiledQueries.ByCity,
            "RecentOrders" => CompiledQueries.RecentOrders,
            _ => throw new ArgumentException($"Unknown query: {queryName}")
        };
        
        var bsonParams = parameters == null 
            ? new BsonDocument() 
            : BsonMapper.SerializeParameters(parameters);
        
        return Customers.Find(expr.WithParameters(bsonParams));
    }
}
```

**PRO**:
- ? Performance ottimale (pre-parsed)
- ? Type-checked a compile-time
- ? Riutilizzabili
- ? Testabili in isolamento

**CONTRO**:
- ? Meno flessibile
- ? Richiede pre-registrazione

---

## ?? RACCOMANDAZIONE: APPROCCIO IBRIDO

Combinare tutti e 4 per massima flessibilità:

```csharp
public class CustomerRepository
{
    private readonly AppDbContext _db;
    
    public CustomerRepository(AppDbContext db) => _db = db;
    
    // === 1. String-based per query complesse ===
    public List<Customer> SearchComplex(string searchTerm, int minAge)
    {
        return _db.Customers.Find(
            @"(Name LIKE @search OR Email LIKE @search) 
              AND Age >= @age 
              AND Active = true",
            new { search = $"%{searchTerm}%", age = minAge }
        ).ToList();
    }
    
    // === 2. Fluent per query dinamiche ===
    public List<Customer> GetByFilters(CustomerFilter filter)
    {
        var query = _db.Customers.Query();
        
        if (filter.MinAge.HasValue)
            query = query.Where(c => c.Property("Age").GreaterThanOrEqual(filter.MinAge.Value));
        
        if (!string.IsNullOrEmpty(filter.City))
            query = query.Where(c => c.Property("City").Equals(filter.City));
        
        if (filter.VipOnly)
            query = query.Where(c => c.Property("VIP").Equals(true));
        
        return query
            .OrderBy(c => c.Property(filter.SortBy ?? "Name"))
            .Skip(filter.PageSize * filter.PageNumber)
            .Limit(filter.PageSize)
            .ToList();
    }
    
    // === 3. Strongly-typed per query comuni ===
    public List<Customer> GetActiveByCity(string city)
    {
        return _db.Customers
            .Where(x => x.City, op => op.Equals(city))
            .Where(x => x.Active, op => op.Equals(true))
            .OrderBy(x => x.Name)
            .ToList();
    }
    
    // === 4. Pre-compiled per query critiche ===
    public List<Customer> GetTopSpenders(int year)
    {
        return _db.Customers
            .ExecuteQuery("TopSpendersByYear", new { year })
            .ToList();
    }
}
```

---

## ?? COMPARISON TABLE

| Feature | String-Based | Fluent Builder | Strongly-Typed | Pre-Compiled |
|---------|-------------|----------------|----------------|--------------|
| **AOT Compatible** | ? | ? | ? | ? |
| **Type Safety** | ? | ?? Parziale | ? | ? |
| **IntelliSense** | ? | ? | ? | ? |
| **Flexibility** | ??? | ?? | ? | ? |
| **Performance** | ?? Parse runtime | ?? Build runtime | ?? Build runtime | ? Pre-parsed |
| **Verbosity** | ? Conciso | ? Verboso | ?? Medio | ? Conciso |
| **Learning Curve** | ? Basso | ?? Medio | ? Basso | ?? Medio |
| **Best For** | Complex queries | Dynamic filters | Common queries | Critical queries |

---

## ?? IMPLEMENTAZIONE PRIORITY

### Phase 1: MVP (2-3 settimane)
1. ? **String-based queries** - Già supportato da BsonExpression
2. ? **FindById, FindAll** - API semplici

### Phase 2: Developer Experience (2-3 settimane)
3. ? **Fluent Query Builder** - Nuova implementazione
4. ? **Helper methods** - Contains, StartsWith, Between, etc.

### Phase 3: Type Safety (2-3 settimane)
5. ? **Strongly-typed extensions** - Con Source Generator
6. ? **Validation** - Type-checked property names

### Phase 4: Optimization (1-2 settimane)
7. ? **Pre-compiled queries** - Per use cases critici
8. ? **Query caching** - Per performance

---

## ?? ESEMPIO FINALE: Customer Service

```csharp
// Domain layer - Clean
namespace MyApp.Domain
{
    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public int Age { get; set; }
        public string City { get; set; }
        public bool VIP { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<Order> Orders { get; set; }
    }
}

// Infrastructure - DbContext
namespace MyApp.Infrastructure
{
    public class AppDbContext : LiteDbContext
    {
        public ILiteCollection<Customer> Customers => Collection<Customer>();
        public ILiteCollection<Order> Orders => Collection<Order>();
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(entity =>
            {
                entity.HasKey(x => x.Id).AutoIncrement();
                entity.Property(x => x.Email).HasIndex().IsUnique();
                
                // Pre-compiled queries
                entity.HasQuery("TopVIPCustomers",
                    q => q.Where("VIP = true AND Orders[*].Total > 10000")
                          .OrderByDescending("Orders[*].Total")
                          .Limit(100));
            });
        }
    }
}

// Application - Service
namespace MyApp.Services
{
    public class CustomerService
    {
        private readonly AppDbContext _db;
        
        public CustomerService(AppDbContext db) => _db = db;
        
        // Simple lookup
        public Customer GetById(int id) => _db.Customers.FindById(id);
        
        // String-based complex query
        public List<Customer> SearchCustomers(string term)
        {
            return _db.Customers.Find(
                "Name LIKE @term OR Email LIKE @term",
                new { term = $"%{term}%" }
            ).ToList();
        }
        
        // Fluent builder for dynamic filters
        public List<Customer> GetFiltered(CustomerFilter filter)
        {
            var query = _db.Customers.Query();
            
            if (filter.MinAge.HasValue)
                query = query.Where(c => c.Property("Age").GreaterThan(filter.MinAge.Value));
            
            if (!string.IsNullOrEmpty(filter.City))
                query = query.Where(c => c.Property("City").Equals(filter.City));
            
            return query.ToList();
        }
        
        // Strongly-typed common query
        public List<Customer> GetActiveInCity(string city)
        {
            return _db.Customers
                .Where(x => x.City, op => op.Equals(city))
                .Where(x => x.Active, op => op.Equals(true))
                .OrderBy(x => x.Name)
                .ToList();
        }
        
        // Pre-compiled critical query
        public List<Customer> GetTopVIPs()
        {
            return _db.Customers
                .ExecuteQuery("TopVIPCustomers")
                .ToList();
        }
    }
}
```

---

## ?? CONCLUSIONI

**Strategia raccomandata**: **Approccio Ibrido**

1. **String-based** come foundation (già presente in BsonExpression)
2. **Fluent builder** per developer experience
3. **Strongly-typed** per common patterns
4. **Pre-compiled** per query critiche

Questo approccio:
- ? È completamente AOT-compatible
- ? Offre flessibilità (string) e type-safety (strongly-typed)
- ? Ha performance ottime (pre-compiled)
- ? È progressivamente adottabile (si parte da string-based)

**Next step**: Vuoi che inizi a implementare il prototype del Query Builder?
