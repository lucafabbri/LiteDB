# Architettura a Layers per AOT Compatibility

## Strategia: Stack Separation

Invece di riscrivere tutto, separiamo LiteDB in layers chiari e **manteniamo il core engine immutato**.

---

## ?? LAYER ANALYSIS

### Layer 1: Storage Engine (MANTIENI) ?

**File/Namespace**: `LiteDB.Engine.*`

**Caratteristiche**:
- ? Già AOT-compatible al 95%
- ? Operazioni su byte arrays
- ? Strutture concrete (no generics complessi)
- ? Zero reflection dinamica
- ? Maturo e battle-tested

**Componenti**:
```
LiteDB.Engine/
??? Disk/
?   ??? DiskService.cs              ? Binary I/O
?   ??? PageBuffer.cs               ? Unsafe buffer operations
?   ??? MemoryCache.cs              ? Dictionary-based cache
?   ??? Streams/
?       ??? AesStream.cs            ? Encryption
?       ??? TempStream.cs           ? Temp file handling
?
??? Pages/
?   ??? BasePage.cs                 ? Fixed offsets, no reflection
?   ??? HeaderPage.cs               ? Database metadata
?   ??? CollectionPage.cs           ? Collection header
?   ??? IndexPage.cs                ? B+Tree nodes
?   ??? DataPage.cs                 ? Document storage
?   ??? VectorIndexPage.cs          ? Vector search (new)
?
??? Services/
?   ??? TransactionService.cs       ? ACID transactions
?   ??? IndexService.cs             ? B+Tree implementation
?   ??? DataService.cs              ? CRUD operations on pages
?   ??? LockService.cs              ? Concurrency control
?   ??? CollectionService.cs        ? Collection management
?   ??? WalIndexService.cs          ? Write-ahead log
?   ??? VectorIndexService.cs       ? Vector operations
?
??? Structures/
?   ??? PageAddress.cs              ? Value type
?   ??? PagePosition.cs             ? Value type
?   ??? DataBlock.cs                ? Struct
?   ??? IndexNode.cs                ? Struct
?   ??? CollectionIndex.cs          ? Index metadata
?   ??? TransactionPages.cs         ? Transaction state
?
??? Query/
    ??? Query.cs                     ? Query execution plan
    ??? QueryExecutor.cs             ? Index selection
    ??? QueryOptimization.cs         ? Cost-based optimizer
    ??? IndexQuery/
        ??? IndexEquals.cs           ? Equality search
        ??? IndexRange.cs            ? Range queries
        ??? IndexScan.cs             ? Full scan
```

**Effort per AOT**: ? MINIMO (1-2 settimane per verifiche)

**Problemi potenziali**:
- ?? Alcune eccezioni con reflection in error messages (facile fix)
- ?? Attributi di debug (rimuovibili)

---

### Layer 2: Document Model (MANTIENI) ?

**File/Namespace**: `LiteDB.Document.*`

**Caratteristiche**:
- ? BsonDocument/BsonValue sono struct/class semplici
- ? No reflection
- ? JSON parser è string-based

**Componenti**:
```
LiteDB.Document/
??? BsonValue.cs                    ? Discriminated union pattern
??? BsonDocument.cs                 ? Dictionary<string, BsonValue>
??? BsonArray.cs                    ? List<BsonValue>
??? ObjectId.cs                     ? 12-byte struct
??? BsonVector.cs                   ? Float array wrapper
?
??? Expression/
?   ??? BsonExpression.cs           ?? PROBLEMATICO (usa Expression.Compile)
?   ??? BsonExpressionParser.cs     ? String parser OK
?   ??? Methods/
?       ??? String.cs                ? String functions
?       ??? Math.cs                  ? Math functions
?       ??? Date.cs                  ? Date functions
?       ??? Aggregate.cs             ? Aggregation functions
?
??? Json/
    ??? JsonReader.cs               ? Manual parsing
    ??? JsonWriter.cs               ? StringBuilder-based
    ??? JsonSerializer.cs           ? Static methods
```

**Effort per AOT**: ?? BASSO (2-3 settimane)

**Azioni necessarie**:
- ?? BsonExpression: Rimuovere Expression.Compile(), usare interpreted mode
- ? Resto già compatibile

---

### Layer 3: Client API - LEGACY (DEPRECARE) ??

**File/Namespace**: `LiteDB.Client.*` (current)

**Caratteristiche**:
- ? Pesante uso di reflection
- ? Expression.Compile() ovunque
- ? LINQ to BsonExpression con reflection
- ? Auto-discovery di properties

**Componenti da deprecare**:
```
LiteDB.Client.Legacy/
??? Mapper/
?   ??? BsonMapper.cs               ? Core del problema
?   ??? EntityMapper.cs             ? Auto-discovery
?   ??? MemberMapper.cs             ? Getters/setters dinamici
?   ??? Reflection/
?   ?   ??? Reflection.cs           ? CreateInstance dinamico
?   ?   ??? Reflection.Expression.cs ? Expression.Compile()
?   ??? Linq/
?       ??? LinqExpressionVisitor.cs ? Expression tree walking
?       ??? TypeResolver/           ? Type inspection runtime
?
??? Database/
    ??? LiteDatabase.cs             ?? API entry point (wrappare)
    ??? LiteCollection.cs           ?? LINQ methods (wrappare)
```

**Strategia**: 
1. Marcare come `[Obsolete]` in v6.0
2. Mantenere funzionante per backwards compatibility
3. Redirigere documentazione verso nuovo stack

---

### Layer 4: Client API - AOT (NUOVO) ??

**File/Namespace**: `LiteDB.Aot.*` (nuovo namespace)

**Caratteristiche**:
- ? Zero reflection runtime
- ? Configuration-based
- ? Source generator assisted
- ? Type-safe

**Nuovo stack**:
```
LiteDB.Aot/
??? Context/
?   ??? LiteDbContext.cs            ?? Base class (stile EF)
?   ??? LiteDbContextOptions.cs     ?? Configuration builder
?   ??? ModelBuilder/
?       ??? ModelBuilder.cs         ?? Fluent configuration
?       ??? EntityTypeBuilder.cs    ?? Per-entity config
?       ??? PropertyBuilder.cs      ?? Per-property config
?       ??? IEntityTypeConfiguration.cs ?? Configuration classes
?
??? Mapping/
?   ??? IEntityMapper.cs            ?? Interface per mappers
?   ??? EntityMapperRegistry.cs     ?? Type-safe registration
?   ??? SerializationContext.cs     ?? Serialization state
?   ??? Conventions/
?       ??? CamelCaseConvention.cs  ?? Naming conventions
?       ??? IgnoreNullConvention.cs ?? Null handling
?
??? Collections/
?   ??? LiteCollection.cs           ?? Type-safe collection
?   ??? LiteCollectionAsync.cs      ?? Async operations
?   ??? CollectionSet.cs            ?? DbContext.Customers property
?
??? Query/
?   ??? QueryBuilder.cs             ?? Fluent query API
?   ??? IQueryable.cs               ?? Subset of LINQ support
?   ??? StringQueryExtensions.cs    ?? String-based queries
?
??? ChangeTracking/
    ??? ChangeTracker.cs            ?? Optional: EF-style tracking
    ??? EntityState.cs              ?? Added/Modified/Deleted
```

**Effort**: ???? ALTO (4-6 settimane)

---

## ?? COMPARISON: Vecchio vs Nuovo Stack

### Vecchio Stack (Reflection-based)

```csharp
// ? Reflection automatica
public class Customer 
{
    public int Id { get; set; }
    public string Name { get; set; }
}

var db = new LiteDatabase("app.db");
var customers = db.GetCollection<Customer>();

// Auto-mapping con reflection
customers.Insert(new Customer { Id = 1, Name = "John" });

// LINQ to BsonExpression con reflection
var results = customers.Find(x => x.Name.StartsWith("J"));
```

**Problemi AOT**:
- `GetProperties()` per auto-discover membri
- `Expression.Compile()` per generare getters/setters
- Type inspection runtime

---

### Nuovo Stack (Configuration-based)

```csharp
// ? Clean domain entity (zero dipendenze)
public class Customer 
{
    public int Id { get; set; }
    public string Name { get; set; }
}

// ? Infrastructure layer - Configurazione separata
public class AppDbContext : LiteDbContext
{
    public ILiteCollection<Customer> Customers => Collection<Customer>();
    
    protected override void OnConfiguring(LiteDbContextOptionsBuilder options)
    {
        options.UseDatabase("app.db");
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configurazione esplicita - Source Generator usa questo
        modelBuilder.Entity<Customer>(entity => 
        {
            entity.HasKey(e => e.Id).AutoIncrement();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });
    }
}

// Uso
using var context = new AppDbContext();

// Insert - usa mapper generato
context.Customers.Insert(new Customer { Name = "John" });

// Query - string-based (AOT-safe)
var results = context.Customers.Find("Name LIKE 'J%'");

// Oppure: Query builder fluent
var results2 = context.Customers.Query()
    .Where("Name").StartsWith("J")
    .ToList();
```

**Vantaggi AOT**:
- Configuration è compile-time known
- Source Generator genera serializers concreti
- Zero reflection runtime
- Type-safe ancora grazie a Expression<Func<>> in config

---

## ?? SOURCE GENERATOR

Il Source Generator analizza `OnModelCreating` e genera:

```csharp
// Auto-generated: AppDbContext.g.cs
partial class AppDbContext
{
    // Generated mapper per Customer
    private sealed class CustomerMapper : IEntityMapper<Customer>
    {
        public BsonDocument Serialize(Customer entity)
        {
            return new BsonDocument
            {
                ["_id"] = new BsonValue(entity.Id),
                ["Name"] = new BsonValue(entity.Name)
            };
        }
        
        public Customer Deserialize(BsonDocument document)
        {
            return new Customer
            {
                Id = document["_id"].AsInt32,
                Name = document["Name"].AsString
            };
        }
        
        public void Validate(Customer entity)
        {
            if (string.IsNullOrEmpty(entity.Name))
                throw new ValidationException("Name is required");
            if (entity.Name.Length > 100)
                throw new ValidationException("Name max length is 100");
        }
    }
    
    // Auto-registration
    protected override void RegisterMappers()
    {
        RegisterMapper<Customer>(new CustomerMapper());
    }
}
```

**Tutto noto a compile-time = AOT compatible!**

---

## ?? EFFORT BREAKDOWN

### Opzione A: Riscrivere TUTTO
```
Storage Engine:      4-6 mesi   ?
Document Model:      2-3 mesi   ?
Client API:          4-6 mesi   ?
Testing:             2-3 mesi   ?
Migration:           2-3 mesi   ?
????????????????????????????????
TOTALE:            14-21 mesi   ??
Rischio:             ALTISSIMO  ??
Backwards compat:    ROTTA      ?
```

### Opzione B: Nuovo Stack su Core Esistente (RACCOMANDATO)
```
Verifica Engine:     1-2 sett   ?
Fix Document Model:  2-3 sett   ?
Nuovo Client Stack:  4-6 sett   ?
Source Generator:    3-4 sett   ?
Testing:             3-4 sett   ?
Migration tools:     2-3 sett   ?
????????????????????????????????
TOTALE:             4-6 mesi    ??
Rischio:             BASSO      ??
Backwards compat:    MANTENUTA  ?
DB Format:           IMMUTATO   ?
```

---

## ?? MIGRATION STRATEGY

### Fase 1: Dual Stack (v6.0-beta)
```csharp
// Both APIs work with same .db files
// Package: LiteDB v6.0

// Legacy API (works, but shows obsolete warnings)
[Obsolete("Use LiteDbContext for AOT compatibility")]
var db = new LiteDatabase("app.db");

// New API (AOT-compatible)
var context = new AppDbContext("app.db");
```

### Fase 2: Deprecation (v6.x)
- Documenti migration guide
- Tooling per auto-convert
- Legacy API ancora funzionante

### Fase 3: Removal (v7.0)
- Rimuovere legacy stack
- Solo AOT API

---

## ?? CONCLUSIONI

**RACCOMANDAZIONE**: Nuovo stack sopra core esistente

**Motivi**:
1. ? **Effort 3x più basso** (4-6 mesi vs 14-21 mesi)
2. ? **Rischio minimizzato** (core engine già testato)
3. ? **Backwards compatibility** (stesso formato .db)
4. ? **Gradualità** (dual stack durante migrazione)
5. ? **Clean Architecture** (rispettata con DbContext pattern)

**Prossimo step**: Iniziare con prototype di:
1. `LiteDbContext` base class
2. `ModelBuilder` API
3. Source Generator basic
4. Integration test che prova entrambi gli stack sullo stesso .db

---

**Performance Note**: Il nuovo stack potrebbe essere **più veloce** del vecchio perché:
- Nessun overhead di reflection runtime
- Serializers generati sono più diretti
- Meno allocazioni (no boxing per delegates)

**Benchmark attesi**:
```
Serialization:   +15-25% faster  ??
Deserialization: +20-30% faster  ??
Query:           Similar         ??
Storage:         Identical       ?
```
