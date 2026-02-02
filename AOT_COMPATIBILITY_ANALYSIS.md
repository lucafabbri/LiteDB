# Analisi Compatibilità AOT per LiteDB

## Sommario Esecutivo

Questo documento analizza i punti critici per rendere LiteDB compatibile con Native AOT (Ahead-Of-Time) in .NET 8.0. 
La soluzione attualmente si basa pesantemente su reflection e espressioni dinamiche, che sono incompatibili con AOT.

**Stato Attuale**: ? Non compatibile AOT
**Target Framework Attuale**: netstandard2.0 + net8.0
**Livello di Effort Stimato**: ALTO (Refactoring significativo richiesto)

---

## ?? PUNTI CRITICI - Alta Priorità

### 1. Sistema di Mapping BsonMapper (CRITICO)

**File Coinvolti**:
- `LiteDB\Client\Mapper\BsonMapper.cs`
- `LiteDB\Client\Mapper\BsonMapper.GetEntityMapper.cs`
- `LiteDB\Client\Mapper\BsonMapper.Serialize.cs`
- `LiteDB\Client\Mapper\BsonMapper.Deserialize.cs`
- `LiteDB\Client\Mapper\EntityMapper.cs`
- `LiteDB\Client\Mapper\MemberMapper.cs`

**Problemi**:
```csharp
// ? Uso intensivo di reflection per discovery dei membri
protected virtual IEnumerable<MemberInfo> GetTypeMembers(Type type)
{
    var flags = this.IncludeNonPublic
        ? (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        : (BindingFlags.Public | BindingFlags.Instance);

    members.AddRange(type.GetProperties(flags)
        .Where(x => x.CanRead && x.GetIndexParameters().Length == 0)
        .Select(x => x as MemberInfo));
}

// ? GetCustomAttributes - incompatibile con AOT
if (ignoreAttrs.Any(ia => CustomAttributeExtensions.IsDefined(memberInfo, ia, true)))

// ? Lookup di attributi per nome (peggiore per AOT)
private static bool HasAttributeByName(MemberInfo member, IEnumerable<string> attributeFullNames)
{
    var attrs = member.GetCustomAttributes(false);
    var attrTypeNames = new HashSet<string>(attrs.Select(a => a.GetType().FullName));
    return attributeFullNames.Any(name => attrTypeNames.Contains(name));
}

// ? Type.GetTypeInfo() - problematico in AOT
var typeInfo = type.GetTypeInfo();
if (typeInfo.IsClass) { ... }
```

**Impatto**: ?? BLOCCANTE
**Feature Affette**: 
- Serializzazione/Deserializzazione automatica
- Mapping entity-documento
- Attributi custom ([BsonId], [BsonIgnore], [BsonField])
- Auto-discovery di proprietà ID

**Soluzioni Alternative**:

1. **DbContext Pattern con Source Generators** (? RACCOMANDATO - Rispetta Clean Architecture)
   ```csharp
   // Entità di dominio - ZERO dipendenze
   public class Customer { public int Id { get; set; } }
   
   // Infrastructure - Configuration
   public class AppDbContext : LiteDbContext
   {
       public ILiteCollection<Customer> Customers => GetCollection<Customer>();
       
       protected override void OnModelCreating(ModelBuilder builder)
       {
           builder.Entity<Customer>(e => e.HasKey(x => x.Id));
           // Source Generator genera mapping da questa configurazione
       }
   }
   ```
   
2. **Fluent Registration API** (Alternativa senza Context)
   ```csharp
   var mapper = new BsonMapper();
   
   // Configurazione esplicita - nessuna reflection runtime
   mapper.Entity<Customer>(entity => {
       entity.Id(x => x.Id, autoId: true);
       entity.Property(x => x.Name);
       entity.Ignore(x => x.TemporaryData);
   });
   
   // Source Generator analizza queste chiamate e genera serializers
   ```

3. **Configuration Classes** (Pattern EF Core)
   ```csharp
   public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
   {
       public void Configure(EntityTypeBuilder<Customer> builder)
       {
           builder.HasKey(x => x.Id);
           builder.Property(x => x.Name).HasMaxLength(100);
       }
   }
   
   // Nel context:
   modelBuilder.ApplyConfiguration(new CustomerConfiguration());
   ```

**Vantaggi di questo approccio**:
- ? **Clean Architecture**: Dominio senza dipendenze infrastrutturali
- ? **AOT Compatible**: Tutto noto a compile-time
- ? **Type-Safe**: Expressions lambda per configurazione
- ? **Familiare**: Stesso pattern di EF Core
- ? **Testabile**: Configurazione separata dal dominio

---

### 2. Sistema Reflection con Expression Compilation (CRITICO)

**File Coinvolti**:
- `LiteDB\Client\Mapper\Reflection\Reflection.cs`
- `LiteDB\Client\Mapper\Reflection\Reflection.Expression.cs`

**Problemi**:
```csharp
// ? CreateInstance con reflection cache
private static readonly Dictionary<Type, CreateObject> _cacheCtor = 
    new Dictionary<Type, CreateObject>();

public static object CreateInstance(Type type)
{
    if (_cacheCtor.TryGetValue(type, out CreateObject c))
    {
        return c(null);
    }
    // ... usa GetTypeInfo(), IsClass, IsInterface
}

// ? Expression.Compile() - NON supportato in AOT
public static CreateObject CreateClass(Type type)
{
    var pDoc = Expression.Parameter(typeof(BsonDocument), "_doc");
    return Expression.Lambda<CreateObject>(Expression.New(type), pDoc).Compile();
}

// ? Dynamic getter/setter con expressions
public static GenericGetter CreateGenericGetter(Type type, MemberInfo memberInfo)
{
    var obj = Expression.Parameter(typeof(object), "o");
    var accessor = Expression.MakeMemberAccess(
        Expression.Convert(obj, memberInfo.DeclaringType), memberInfo);
    return Expression.Lambda<GenericGetter>(
        Expression.Convert(accessor, typeof(object)), obj).Compile();
}

public static GenericSetter CreateGenericSetter(Type type, MemberInfo memberInfo)
{
    // ... stesso problema con Expression.Lambda().Compile()
}

// ? Uso di MakeGenericType
public static Type GetGenericListOfType(Type type)
{
    var listType = typeof(List<>);
    return listType.MakeGenericType(type); // ? Non AOT-friendly
}
```

**Impatto**: ?? BLOCCANTE
**Performance Hit**: Questo è il cuore della performance di LiteDB - passare a alternative più lente potrebbe degradare significativamente le prestazioni

**Soluzioni Alternative**:
1. **Precompilazione con Source Generators**
2. **Interface-based approach**: Richiedere `ILiteDBSerializable<T>` per AOT mode
3. **Fallback a Reflection pura** (più lento ma funziona in AOT con warning suppressions)

---

### 3. LINQ to BsonExpression Conversion (ALTO)

**File Coinvolti**:
- `LiteDB\Client\Mapper\Linq\LinqExpressionVisitor.cs`
- `LiteDB\Document\Expression\BsonExpression.cs`
- Tutti i TypeResolver in `LiteDB\Client\Mapper\Linq\TypeResolver\*`

**Problemi**:
```csharp
// ? Visita expression tree e usa reflection per type resolution
private static readonly Dictionary<Type, ITypeResolver> _resolver = 
    new Dictionary<Type, ITypeResolver>
{
    [typeof(BsonValue)] = new BsonValueResolver(),
    [typeof(DateTime)] = new DateTimeResolver(),
    // ...
};

// ? Uso di LambdaExpression.Compile()
protected override Expression VisitLambda<T>(Expression<T> node)
{
    var l = base.VisitLambda(node);
    // ... manipolazione expression tree
}

// ? Type checking con GetType()
if (type.GetTypeInfo().IsGenericType) {
    Type[] generics = type.GetGenericArguments();
    valueType = generics[1];
}
```

**Impatto**: ?? ALTO
**Feature Affette**:
- Queries LINQ: `collection.Find(x => x.Age > 18)`
- Include: `collection.Include(x => x.Customer)`
- OrderBy/Select con expressions

**Soluzioni Alternative**:
1. **String-based queries only** (come MongoDB driver fa per AOT)
   ```csharp
   // Invece di: collection.Find(x => x.Age > 18)
   collection.Find("Age > 18")
   ```
2. **Query Builder Pattern**
   ```csharp
   collection.Query()
       .Where("Age").GreaterThan(18)
       .ToList();
   ```

---

### 4. Type Discovery e Costruttori Parametrizzati (MEDIO)

**File Coinvolti**:
- `LiteDB\Client\Mapper\BsonMapper.GetEntityMapper.cs` (GetTypeCtor method)

**Problemi**:
```csharp
// ? Enumerazione costruttori runtime
protected virtual CreateObject GetTypeCtor(EntityMapper mapper)
{
    Type type = mapper.ForType;
    foreach (ConstructorInfo ctor in type.GetConstructors())
    {
        ParameterInfo[] pars = ctor.GetParameters();
        // ... matching parametri con membri
        
        // ? Mapping parametri per nome (case-insensitive reflection)
        foreach (MemberMapper member in mapper.Members)
        {
            if (member.MemberName.ToLower() == par.Name.ToLower() 
                && member.DataType == par.ParameterType)
            {
                // ...
            }
        }
    }
}
```

**Impatto**: ?? MEDIO
**Feature Affette**:
- Costruttori con parametri
- Immutable objects
- Attributo [BsonCtor]

**Soluzioni Alternative**:
1. Richiedere costruttore parameterless per AOT
2. Factory method registration API
3. Source generator per costruttori custom

---

### 5. Custom Type Serializers (MEDIO)

**File Coinvolti**:
- `LiteDB\Client\Mapper\BsonMapper.cs`

**Problemi**:
```csharp
// ? Questo pattern è OK, ma dipende da come viene popolato
private readonly ConcurrentDictionary<Type, Func<object, BsonValue>> _customSerializer 
    = new ConcurrentDictionary<Type, Func<object, BsonValue>>();

// ? Se popolato dinamicamente basato su reflection
public void RegisterType<T>(Func<T, BsonValue> serialize, Func<BsonValue, T> deserialize)
{
    var type = typeof(T); // OK
    _customSerializer[type] = obj => serialize((T)obj); // OK
}

// Ma è spesso usato con:
if (_customSerializer.TryGetValue(type, out var custom) || 
    _customSerializer.TryGetValue(obj.GetType(), out custom)) // ? GetType() in hot path
```

**Impatto**: ?? MEDIO
**Soluzioni**: 
- Continua a supportare, ma documenta limitazioni
- Richiedi registrazione esplicita dei tipi in AOT mode

---

## ?? PUNTI PROBLEMATICI - Media Priorità

### 6. Enum Handling

**File**: `LiteDB\Client\Mapper\BsonMapper.Serialize.cs`

```csharp
// ?? Enum.ToString() e parsing - problematico ma gestibile
else if (obj is Enum)
{
    if (EnumAsInteger)
    {
        return new BsonValue((int)obj); // ? OK
    }
    else
    {
        return new BsonValue(obj.ToString()); // ?? ToString OK, parsing problematico
    }
}
```

**Impatto**: ?? MEDIO
**Soluzione**: Forzare EnumAsInteger = true in AOT mode

---

### 7. Dictionary Generic Arguments Inspection

**File**: `LiteDB\Client\Mapper\BsonMapper.Serialize.cs`

```csharp
// ? Reflection su generic arguments
else if (obj is IDictionary dict)
{
    Type valueType = typeof(object);
    
    if (type.GetTypeInfo().IsGenericType) {
        Type[] generics = type.GetGenericArguments(); // ?
        valueType = generics[1];
    }
}
```

**Impatto**: ?? MEDIO
**Soluzione**: Source generators o typed dictionaries

---

### 8. IEnumerable Type Detection

**File**: `LiteDB\Utils\Extensions\TypeInfoExtensions.cs`, `LiteDB\Client\Mapper\Reflection\Reflection.cs`

```csharp
// ?? Reflection su interfacce
public static Type GetListItemType(Type listType)
{
    if (listType.IsArray) return listType.GetElementType(); // ?
    
    foreach (var i in listType.GetInterfaces()) // ?
    {
        if (i.GetTypeInfo().IsGenericType && 
            i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return i.GetGenericArguments()[0]; // ?
        }
    }
}
```

**Impatto**: ?? MEDIO

---

### 9. BsonExpression Dynamic Compilation

**File**: `LiteDB\Document\Expression\BsonExpression.cs`

```csharp
// ? Expression compilation per formule
internal Expression Expression { get; set; }

// Usato per:
internal delegate IEnumerable<BsonValue> BsonExpressionEnumerableDelegate(...);
internal delegate BsonValue BsonExpressionScalarDelegate(...);
```

**Impatto**: ?? MEDIO
**Feature Affette**: Expressions complesse, computed fields

---

### 10. Anonymous Types Detection

**File**: `LiteDB\Utils\Extensions\TypeInfoExtensions.cs`

```csharp
// ?? Detection di anonymous types
public static bool IsAnonymousType(this Type type)
{
    bool isAnonymousType = 
        type.FullName.Contains("AnonymousType") && // ?? Fragile
        type.GetTypeInfo().GetCustomAttributes(
            typeof(CompilerGeneratedAttribute), false).Any(); // ?
    
    return isAnonymousType;
}
```

**Impatto**: ?? BASSO (anonymous types non sono supportati in AOT comunque)

---

## ?? AREE COMPATIBILI o A BASSO RISCHIO

### 11. Serializzazione di Tipi Primitivi

```csharp
// ? Pattern matching su tipi concreti - AOT friendly
else if (obj is Int32) return new BsonValue((Int32)obj);
else if (obj is Int64) return new BsonValue((Int64)obj);
else if (obj is String) return new BsonValue((String)obj);
// ... etc
```

**Stato**: ? Già AOT compatible

---

### 12. BsonDocument/BsonValue Core

I tipi core `BsonDocument`, `BsonValue`, `BsonArray` non usano reflection.

**Stato**: ? Già AOT compatible

---

### 13. Storage Engine e I/O

File come:
- `LiteDB\Engine\*`
- `LiteDB\Engine\Disk\*`
- Binary serialization

**Stato**: ? Prevalentemente AOT compatible (verificare comunque)

---

## ?? PIANO DI MIGRAZIONE RACCOMANDATO

### Fase 1: Foundation (NET 8.0 Only Build) ?? 1-2 settimane

1. **Creare branch AOT-compatible**
   - Target solo net8.0
   - Rimuovere netstandard2.0 temporaneamente

2. **Aggiungere attributi AOT al project**
   ```xml
   <PropertyGroup>
       <PublishAot>true</PublishAot>
       <IsAotCompatible>true</IsAotCompatible>
   </PropertyGroup>
   ```

3. **Analisi statica**
   - Compilare con analisi AOT abilitata
   - Documentare tutti i warnings ILC (IL Compiler)

### Fase 2: Core API Redesign ?? 4-6 settimane

4. **Nuovo sistema di mapping basato su Source Generators**
   ```csharp
   [LiteDBEntity]
   public partial class Customer 
   {
       [BsonId]
       public int Id { get; set; }
       
       public string Name { get; set; }
       
       [BsonIgnore]
       public string Temp { get; set; }
   }
   
   // Auto-generated:
   // partial class Customer : ILiteDBSerializable<Customer> { ... }
   ```

5. **Manual Registration API (fallback)**
   ```csharp
   BsonMapper.Configure<Customer>(config => {
       config.Id(x => x.Id);
       config.Property(x => x.Name);
       config.Ignore(x => x.Temp);
   });
   ```

6. **String-based query API**
   ```csharp
   // Mantenere LINQ quando possibile con limiti
   // Fornire alternative string-based
   collection.Find("$.Age > @age", new { age = 18 });
   ```

### Fase 3: Feature Parity ?? 4-6 settimane

7. **Reimplementare features critiche**
   - Serializzazione/Deserializzazione con generators
   - Query system semplificato
   - Include/DBRef con API esplicite

8. **Testing intensivo**
   - Unit tests per tutte le API
   - Integration tests
   - Performance benchmarking vs versione reflection

### Fase 4: Backwards Compatibility ?? 2-4 settimane

9. **API compatibility layer**
   - Mantenere vecchie API dove possibile
   - Deprecation warnings chiare
   - Migration guide

10. **Documentation**
    - AOT limitations document
    - Migration guide from reflection-based
    - Best practices

---

## ?? FEATURE TRADEOFFS (Da Considerare)

### Features da Rimuovere/Limitare in AOT Mode:

? **Auto-discovery di ID property** (senza attributo)
```csharp
// NON supportato in AOT:
public class Customer {
    public int CustomerId { get; set; } // auto-detected come ID
}

// Richiedere:
public class Customer {
    [BsonId]
    public int CustomerId { get; set; }
}
```

? **LINQ Expressions complesse**
```csharp
// NON supportato in AOT:
.Find(x => x.Customer.Orders.Any(o => o.Total > 100))

// Alternativa:
.Find("$.Customer.Orders[*].Total > 100")
```

? **Anonymous types in queries**
```csharp
// NON supportato:
.Select(x => new { x.Name, x.Age })

// Alternativa: classi concrete o record
```

? **Costruttori parametrizzati auto-mapping**

?? **Limitato**: Custom attributes da assembly esterni (richiede pre-registration)

---

## ?? MATRICE RISCHIO/SFORZO

| Feature | Rischio AOT | Effort | Priorità | Soluzione Raccomandata |
|---------|------------|--------|----------|----------------------|
| BsonMapper Core | ?? Alto | Alto | P0 | Source Generators |
| Expression Getters/Setters | ?? Alto | Alto | P0 | Source Generators |
| LINQ to Bson | ?? Medio | Alto | P1 | String queries + Limited LINQ |
| Costruttori custom | ?? Medio | Medio | P2 | Manual factory registration |
| Enum serialization | ?? Basso | Basso | P3 | Force EnumAsInteger |
| Custom type serializers | ?? Basso | Basso | P3 | Keep as-is, document |
| Storage Engine | ?? Basso | Basso | P4 | Minimal changes |

---

## ?? STRUMENTI DI VERIFICA

### Compilazione con AOT Warnings

```bash
dotnet publish -c Release -r win-x64 --self-contained /p:PublishAot=true
```

### Analisi Statica

Cercare questi warnings:
- **IL2026**: Metodo richiede reflection dinamica
- **IL2067**: Type non può essere staticamente determinato
- **IL2070**: `this` type non può essere determinato
- **IL3050**: P/Invoke richiede marshalling dinamico

### Tools
- `dotnet-ilverify`
- ILSpy per analizzare IL generato
- BenchmarkDotNet per performance comparisons

---

## ?? RIFERIMENTI

### Microsoft Docs
- [Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Prepare .NET libraries for trimming](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)
- [Introduction to AOT warnings](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings)

### Source Generator Examples
- System.Text.Json source generators
- Entity Framework Core compiled models
- [Source Generator Cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md)

### Similar Projects
- **MongoDB C# Driver**: Ha una modalità AOT limitata
- **Marten**: In progress AOT support
- **LiteDB Alternative Approach**: Considerare fork specifico per AOT

---

## ? CHECKLIST DECISION POINTS

Prima di procedere, rispondere a queste domande:

- [ ] **Obiettivo**: Compatibilità AOT completa o parziale?
- [ ] **Breaking Changes**: Accettabile avere API v6.0 incompatibile?
- [ ] **Performance**: Qual è il degrado accettabile rispetto a reflection?
- [ ] **Timeline**: 3 mesi? 6 mesi? 1 anno?
- [ ] **Multi-target**: Supportare sia AOT che reflection-based?
- [ ] **Feature Set**: Quali features sono must-have vs nice-to-have?

---

## ?? CONCLUSIONI E RACCOMANDAZIONI

### Scenario 1: AOT Completo (Raccomandato per nuovo progetto)
- Fork il progetto come "LiteDB.Aot"
- Source generators per tutto
- API più esplicite, meno "magic"
- Timeline: 6-12 mesi
- **Pro**: Performance ottimale, dimensioni ridotte, startup veloce
- **Contro**: Breaking changes, effort significativo

### Scenario 2: Dual Mode (Raccomandato per evoluzione)
```csharp
#if AOT_MODE
    // Source generator based
#else
    // Reflection based
#endif
```
- Mantenere entrambe le versioni
- Timeline: 8-12 mesi
- **Pro**: Backward compatibility
- **Contro**: Complessità di manutenzione doppia

### Scenario 3: AOT-Limited (Quick Win)
- Solo storage engine AOT compatible
- Mapping richiede manual registration
- Timeline: 2-3 mesi
- **Pro**: Quick to market, effort ridotto
- **Contro**: Developer experience peggiore

---

## ?? PROSSIMI STEP IMMEDIATI

1. **Decision meeting** con stakeholder
2. **Prototype** Source Generator per EntityMapper
3. **Benchmark** performance impact di alternative a Expression.Compile()
4. **Survey community** per capire adoption di AOT
5. **Create Epic/Issues** su GitHub per tracking

---

**Documento generato**: 2024
**Versione LiteDB analizzata**: Current dev branch (net8.0 target)
**Autore**: Copilot AI Analysis
**Status**: ?? DRAFT - Richiede review da mantainer
