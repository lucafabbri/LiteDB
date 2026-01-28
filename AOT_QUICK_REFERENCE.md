# AOT Quick Reference Guide

## What is Native AOT?

Native AOT (Ahead-of-Time) is a .NET deployment model that compiles your application to native code ahead of time, producing a self-contained executable with no .NET runtime dependency.

**Benefits**:
- ⚡ **Faster startup**: 2-5x faster cold start times
- 💾 **Smaller memory footprint**: 30-50% less memory usage
- 📦 **Smaller binaries**: 70-80% reduction with trimming
- 🚀 **Better performance**: Native code optimizations
- 🔒 **Enhanced security**: No JIT compilation attack surface

**Trade-offs**:
- 🚫 **No dynamic code generation**: Expression.Compile(), Reflection.Emit not supported
- 🔍 **Limited reflection**: Type discovery requires compile-time declaration
- ⏱️ **Longer build times**: Native compilation is slower
- 📝 **More complex code**: Requires attributes and generators

---

## Current LiteDB AOT Status

| Feature | Status | Notes |
|---------|--------|-------|
| Basic CRUD | ❌ Not supported | Requires mapper refactoring |
| Collections | ❌ Not supported | Requires mapper refactoring |
| LINQ Queries | ❌ Not supported | Expression compilation used |
| Custom Types | ❌ Not supported | Reflection-based mapping |
| File Storage | ⚠️ Partial | Core storage may work |
| Encryption | ⚠️ Unknown | Needs testing |

**Summary**: LiteDB is **not currently AOT compatible**. Major refactoring required.

---

## AOT Blockers in LiteDB

### 1. Expression Compilation (Critical)
```csharp
// ❌ NOT AOT Compatible
Expression.Lambda<Func<...>>(...).Compile()
```

**Used in**: Object creation, property getters/setters

**Fix**: Source generators or pre-compiled delegates

---

### 2. Activator.CreateInstance (Critical)
```csharp
// ❌ NOT AOT Compatible
var instance = Activator.CreateInstance(type);
```

**Used in**: Object instantiation, deserialization

**Fix**: Factory pattern with source generators

---

### 3. MakeGenericType (High)
```csharp
// ❌ NOT AOT Compatible
typeof(List<>).MakeGenericType(elementType)
```

**Used in**: Collection creation

**Fix**: Pre-register types or source generators

---

### 4. Type Resolution (Medium)
```csharp
// ❌ NOT AOT Compatible
Type.GetType("MyNamespace.MyClass")
```

**Used in**: Polymorphic deserialization

**Fix**: Type registry

---

## How to Check AOT Compatibility

### Step 1: Enable AOT in Your Project
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

### Step 2: Build and Check for Warnings
```bash
dotnet publish -r linux-x64 -c Release
```

### Step 3: Look for AOT Warnings
```
warning IL2026: Using member 'System.Activator.CreateInstance(Type)' which has 'RequiresDynamicCodeAttribute' can break functionality when AOT compiling.
```

---

## Future AOT-Compatible LiteDB Usage

### Phase 1: Mark Your Classes (Planned)
```csharp
using LiteDB;

[BsonDocument] // Triggers source generator
public partial class Customer // Must be partial
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public List<string> Tags { get; set; }
}
```

### Phase 2: Use Generated Code (Planned)
```csharp
// Source generator creates factory and accessors automatically
using var db = new LiteDatabase("customers.db");
var customers = db.GetCollection<Customer>();

// All mapping code is generated at compile time
customers.Insert(new Customer 
{ 
    Name = "John Doe", 
    Email = "john@example.com",
    Tags = new List<string> { "VIP", "Premium" }
});

var results = customers.Find(x => x.Name.StartsWith("John"));
```

### Phase 3: Publish as Native Executable (Planned)
```bash
# Single command to create native executable
dotnet publish -r win-x64 -c Release

# Result: self-contained .exe with no .NET runtime needed
# Size: ~10MB (vs ~150MB with runtime)
# Startup: <100ms (vs ~1000ms with JIT)
```

---

## Workaround: Use LiteDB in AOT App (Current)

If you need to use LiteDB today in an AOT application, you have limited options:

### Option 1: Disable AOT for LiteDB Assembly
```xml
<ItemGroup>
  <TrimmerRootAssembly Include="LiteDB" />
</ItemGroup>
```
⚠️ **Warning**: This defeats the purpose of AOT and increases binary size significantly.

### Option 2: Use Reflection-Based Fallback
Some AOT scenarios support limited reflection with explicit configuration. Not recommended for LiteDB due to extensive reflection use.

### Option 3: Wait for v6.0
The recommended approach is to wait for LiteDB v6.0 which will include proper AOT support via source generators.

---

## AOT Compatibility Checklist

Use this checklist to evaluate any library for AOT compatibility:

- [ ] No `Expression.Compile()` usage
- [ ] No `Activator.CreateInstance()` without [DynamicallyAccessedMembers]
- [ ] No `Type.MakeGenericType()` with runtime types
- [ ] No `Type.GetType()` with string names
- [ ] No `Reflection.Emit` or `DynamicMethod`
- [ ] No loading assemblies at runtime (`Assembly.Load`)
- [ ] Uses source generators for code generation
- [ ] Marked with `[RequiresDynamicCode]` if non-AOT compatible
- [ ] Documentation mentions AOT compatibility
- [ ] Has AOT test project in CI/CD

**LiteDB Current Score**: 0/10 ❌

**LiteDB v6.0 Target**: 10/10 ✅

---

## Common AOT Patterns

### ✅ AOT-Compatible Factory Pattern
```csharp
public interface IFactory<T>
{
    T Create();
}

public class CustomerFactory : IFactory<Customer>
{
    public Customer Create() => new Customer();
}

// Register at compile time
services.AddSingleton<IFactory<Customer>, CustomerFactory>();
```

### ✅ AOT-Compatible Property Access
```csharp
public interface IPropertyAccessor<T>
{
    object GetValue(T instance, string propertyName);
    void SetValue(T instance, string propertyName, object value);
}

// Generated by source generator
public class CustomerPropertyAccessor : IPropertyAccessor<Customer>
{
    public object GetValue(Customer instance, string propertyName)
    {
        return propertyName switch
        {
            "Id" => instance.Id,
            "Name" => instance.Name,
            _ => throw new ArgumentException($"Unknown property: {propertyName}")
        };
    }
    
    public void SetValue(Customer instance, string propertyName, object value)
    {
        switch (propertyName)
        {
            case "Id": instance.Id = (int)value; break;
            case "Name": instance.Name = (string)value; break;
            default: throw new ArgumentException($"Unknown property: {propertyName}");
        }
    }
}
```

### ✅ AOT-Compatible Type Registry
```csharp
public class TypeRegistry
{
    private readonly Dictionary<string, Type> _types = new();
    
    public void Register<T>()
    {
        _types[typeof(T).FullName] = typeof(T);
    }
    
    public Type GetType(string typeName)
    {
        return _types.TryGetValue(typeName, out var type) 
            ? type 
            : throw new TypeLoadException($"Type not registered: {typeName}");
    }
}

// Usage
var registry = new TypeRegistry();
registry.Register<Customer>();
registry.Register<Order>();
```

---

## Testing for AOT Compatibility

### Create Test Project
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <TrimMode>full</TrimMode>
  </PropertyGroup>
</Project>
```

### Test Scenarios
```csharp
// Test 1: Basic instantiation
var db = new LiteDatabase(":memory:");

// Test 2: CRUD operations
var col = db.GetCollection<Customer>();
col.Insert(new Customer { Name = "Test" });

// Test 3: Queries
var results = col.Find(x => x.Name == "Test");

// Test 4: Serialization/Deserialization
var bson = BsonMapper.Global.ToDocument(new Customer());
var customer = BsonMapper.Global.ToObject<Customer>(bson);
```

### Validate Build
```bash
# Build with AOT
dotnet publish -r linux-x64 -c Release

# Check for warnings
# Should see zero IL2XXX warnings for AOT-compatible code

# Test executable
./bin/Release/net8.0/linux-x64/publish/MyApp
```

---

## FAQ

### Q: When will LiteDB support AOT?
**A**: Planned for LiteDB v6.0, estimated 4-6 months development time.

### Q: Will v5.x users need to migrate?
**A**: v6.0 aims for backward compatibility. AOT support will be opt-in via attributes.

### Q: Can I use LiteDB in an AOT app today?
**A**: Not recommended. Core functionality relies on reflection and expression compilation.

### Q: Will performance improve with AOT?
**A**: Yes. Expected 2-5x faster startup, 30-50% less memory, 10-20% faster runtime.

### Q: Do I need to change my code?
**A**: Minimal changes. Add `[BsonDocument]` attribute and make classes `partial`.

### Q: Will all features work in AOT mode?
**A**: Most features will work. Some advanced scenarios may require reflection mode.

---

## Additional Resources

- [Microsoft: Native AOT Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Microsoft: Prepare Libraries for Trimming](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)
- [Microsoft: AOT Warnings](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings)
- [Microsoft: Source Generators](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)

---

*Last Updated: 2026-01-28*  
*Version: 1.0*
