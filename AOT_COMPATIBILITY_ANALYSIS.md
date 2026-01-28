# LiteDB AOT (Ahead-of-Time) Compilation Compatibility Analysis

## Executive Summary

This document provides a comprehensive analysis of the steps required to make LiteDB compatible with .NET Native AOT compilation. Native AOT is a deployment model that ahead-of-time compiles .NET code to native code, resulting in faster startup times, smaller memory footprints, and self-contained executables.

**Current Status**: LiteDB is **NOT AOT compatible** and would require significant refactoring to support Native AOT.

**Estimated Effort**: Large (Major refactoring required)

---

## Table of Contents

1. [Understanding AOT Constraints](#understanding-aot-constraints)
2. [Current AOT Blockers in LiteDB](#current-aot-blockers-in-litedb)
3. [Required Changes](#required-changes)
4. [Implementation Roadmap](#implementation-roadmap)
5. [Testing Strategy](#testing-strategy)
6. [Alternative Approaches](#alternative-approaches)
7. [References](#references)

---

## Understanding AOT Constraints

Native AOT has several constraints that differ from standard .NET runtime:

### Key Limitations

1. **No Dynamic Code Generation**: Code that uses `Expression.Compile()`, `Reflection.Emit`, or `DynamicMethod` is not supported
2. **Limited Reflection**: Reflection-based activation and type discovery requires source generators or explicit declarations
3. **No Runtime Type Loading**: `Type.GetType()` with string names and `Assembly.Load()` are limited
4. **Trimming-Friendly**: Code must be trim-safe, meaning all used code paths must be statically discoverable
5. **Generic Type Construction**: `Type.MakeGenericType()` requires special handling

### AOT Attributes

To support AOT, code must use specific attributes:
- `[RequiresDynamicCode]` - Marks code that needs JIT compilation
- `[DynamicallyAccessedMembers]` - Preserves members for reflection
- `[UnconditionalSuppressMessage]` - Suppresses trim warnings when safe

---

## Current AOT Blockers in LiteDB

### 1. Dynamic Code Generation via Expression Trees (Critical)

**Location**: `/LiteDB/Client/Mapper/Reflection/Reflection.Expression.cs`

**Issue**: Heavy use of `Expression.Compile()` to generate delegates at runtime:

```csharp
// Line 20: Object instantiation
Expression.Lambda<CreateObject>(Expression.New(type), pDoc).Compile();

// Line 29: Struct instantiation
Expression.Lambda<CreateObject>(convert, pDoc).Compile();

// Line 42: Property/field getters
Expression.Lambda<GenericGetter>(Expression.Convert(accessor, typeof(object)), obj).Compile();

// Line 80: Property/field setters
Expression.Lambda<GenericSetter>(conv, target, value).Compile();
```

**Impact**: Complete blocker - these compiled expressions are core to the mapping system.

**Solution Required**: Replace with source generators or pre-compiled delegates.

---

### 2. Activator.CreateInstance Usage

**Locations**:
- `/LiteDB/Client/Mapper/BsonMapper.Deserialize.cs` - Object instantiation during deserialization
- `/LiteDB/Shell/ShellProgram.cs` - Command instantiation

**Issue**: Dynamic type instantiation without compile-time knowledge:

```csharp
Activator.CreateInstance(type);
```

**Impact**: High - Core functionality for object creation.

**Solution Required**: Source generators to generate factory methods, or manual factory registration.

---

### 3. MakeGenericType and Generic Type Construction

**Location**: `/LiteDB/Client/Mapper/Reflection/Reflection.cs`

**Issue**: Dynamic construction of generic types at runtime:

```csharp
// Lines 152-168
var listType = typeof(List<>);
listType.MakeGenericType(type);

var setType = typeof(HashSet<>);
setType.MakeGenericType(type);

var dictionaryType = typeof(Dictionary<,>);
dictionaryType.MakeGenericType(k, v);
```

**Impact**: High - Used for collection handling in the mapper.

**Solution Required**: Pre-declare common generic types or use source generators.

---

### 4. Type.GetType() for Type Resolution

**Location**: `/LiteDB/Client/Mapper/TypeNameBinder/DefaultTypeNameBinder.cs`

**Issue**: Runtime type resolution from string names:

```csharp
Type.GetType(typeName);
```

**Impact**: Medium - Used for polymorphic deserialization.

**Solution Required**: Type registry with compile-time registration or source generators.

---

### 5. Reflection-Based Member Access

**Locations**: Throughout `/LiteDB/Client/Mapper/` subsystem

**Issue**: Extensive use of reflection to discover and access properties/fields:

```csharp
type.GetProperties()
type.GetFields()
propertyInfo.GetValue()
propertyInfo.SetValue()
```

**Impact**: High - Core mapping functionality relies on reflection.

**Solution Required**: Source generators to create mapping code at compile time.

---

### 6. GetType() for Runtime Type Discovery

**Locations**: Throughout the codebase

**Issue**: Calling `.GetType()` on objects and using reflection on the result.

**Impact**: Medium - Used throughout for polymorphic handling.

**Solution Required**: Explicit type handling or source generators.

---

## Required Changes

To make LiteDB AOT compatible, the following major changes are required:

### Phase 1: Add AOT Diagnostic Attributes (Weeks 1-2)

1. **Update Target Framework**
   - Add `net8.0` or `net9.0` target to `LiteDB.csproj`
   - Keep existing targets for backward compatibility

2. **Add AOT Diagnostic Attributes**
   - Mark non-AOT-compatible methods with `[RequiresDynamicCode]`
   - Add `[DynamicallyAccessedMembers]` to parameters that need reflection
   - Document which APIs are not AOT-compatible

**Example**:
```csharp
[RequiresDynamicCode("BsonMapper uses reflection for object mapping")]
public class BsonMapper
{
    // ...
}
```

### Phase 2: Implement Source Generators (Weeks 3-8)

Create source generators for:

1. **Object Creation Generator**
   - Generate factory methods for all types used with LiteDB
   - Replace `Activator.CreateInstance` and expression compilation

2. **Property/Field Access Generator**
   - Generate optimized getters/setters at compile time
   - Replace reflection-based member access

3. **Generic Type Generator**
   - Pre-generate common generic type combinations
   - Replace `MakeGenericType` calls

**Example Output**:
```csharp
// Generated code
public static class GeneratedMapper_Customer
{
    public static Customer CreateInstance() => new Customer();
    
    public static object GetId(object obj) => ((Customer)obj).Id;
    public static void SetId(object obj, object value) => ((Customer)obj).Id = (int)value;
    
    public static object GetName(object obj) => ((Customer)obj).Name;
    public static void SetName(object obj, object value) => ((Customer)obj).Name = (string)value;
}
```

### Phase 3: Refactor Core Mapping System (Weeks 9-12)

1. **Split Mapper Implementation**
   - Create `BsonMapper` (AOT-compatible, using source generators)
   - Keep `BsonMapperReflection` (non-AOT, using current implementation)
   - Runtime selects appropriate implementation

2. **Add Configuration API**
   ```csharp
   var mapper = BsonMapper.CreateForAOT(); // Uses generated code
   var mapper = BsonMapper.CreateReflection(); // Uses reflection
   ```

3. **Update Type Resolution**
   - Create compile-time type registry
   - Replace `Type.GetType()` with registry lookups

### Phase 4: Add AOT-Compatible Test Project (Weeks 13-14)

1. **Create AOT Test Application**
   - New project: `LiteDB.Tests.AOT`
   - Enable `<PublishAot>true</PublishAot>`
   - Test common scenarios

2. **Add CI/CD Pipeline**
   - Build and test with AOT enabled
   - Ensure no AOT warnings

### Phase 5: Documentation and Samples (Weeks 15-16)

1. **Update Documentation**
   - Add AOT compatibility guide
   - Document limitations and workarounds
   - Provide migration examples

2. **Create Sample Applications**
   - Console app with AOT
   - Web API with AOT
   - Demonstrate best practices

---

## Implementation Roadmap

### Milestone 1: Assessment Complete ✓
- [x] Analyze codebase for AOT blockers
- [x] Document findings
- [x] Create roadmap

### Milestone 2: Diagnostic Phase (Weeks 1-2)
- [ ] Add .NET 8/9 target framework
- [ ] Add AOT diagnostic attributes
- [ ] Create warning/error documentation
- [ ] Set up baseline AOT analysis

### Milestone 3: Source Generator Foundation (Weeks 3-4)
- [ ] Create source generator project
- [ ] Implement basic mapper generator
- [ ] Add unit tests for generator
- [ ] Document generator usage

### Milestone 4: Mapper Generator (Weeks 5-8)
- [ ] Generate object factories
- [ ] Generate property accessors
- [ ] Generate generic type factories
- [ ] Handle nested objects and collections
- [ ] Support custom converters

### Milestone 5: Core Refactoring (Weeks 9-12)
- [ ] Split BsonMapper implementation
- [ ] Add AOT/Reflection mode selection
- [ ] Update type resolution system
- [ ] Refactor problematic reflection code
- [ ] Ensure backward compatibility

### Milestone 6: Testing and Validation (Weeks 13-14)
- [ ] Create AOT test project
- [ ] Test all mapping scenarios
- [ ] Validate performance
- [ ] Test trimming compatibility
- [ ] Add CI/CD pipeline

### Milestone 7: Documentation and Release (Weeks 15-16)
- [ ] Write AOT compatibility guide
- [ ] Create sample applications
- [ ] Update API documentation
- [ ] Prepare release notes
- [ ] Community preview release

---

## Testing Strategy

### Unit Tests
- Test source generators output
- Verify generated code correctness
- Test all mapping scenarios

### Integration Tests
- Full LiteDB operations with AOT
- Performance benchmarks
- Memory usage analysis

### Compatibility Tests
- Ensure backward compatibility
- Test both AOT and reflection modes
- Cross-platform testing (Windows, Linux, macOS)

### CI/CD Integration
- Add AOT build to CI pipeline
- Automated warning detection
- Performance regression tests

---

## Alternative Approaches

### Option 1: Full Source Generator Approach (Recommended)
**Pros**:
- Best AOT performance
- Compile-time safety
- Zero reflection overhead

**Cons**:
- Large development effort
- Requires user code changes (attributes on types)
- Breaking change to API

### Option 2: Hybrid Approach (Recommended)
**Pros**:
- Maintains backward compatibility
- Progressive migration path
- Users choose AOT or reflection

**Cons**:
- Maintains two code paths
- Increased maintenance burden

### Option 3: Partial AOT Support
**Pros**:
- Smaller effort
- Core scenarios work with AOT
- Advanced features use reflection

**Cons**:
- Incomplete AOT support
- Some features unavailable in AOT mode

### Option 4: No AOT Support
**Pros**:
- No development effort
- No breaking changes

**Cons**:
- Cannot be used in AOT applications
- Missing modern .NET feature

---

## Performance Considerations

### Expected Benefits with AOT
- **Startup Time**: 2-5x faster cold start
- **Memory Usage**: 30-50% reduction
- **Binary Size**: 70-80% smaller with trimming
- **Runtime Performance**: 10-20% faster due to optimized native code

### Trade-offs
- **Build Time**: Longer compilation time
- **Development Complexity**: More complex code generation
- **Flexibility**: Reduced runtime flexibility

---

## Migration Path for Users

### Current Code (Non-AOT)
```csharp
using(var db = new LiteDatabase(@"MyData.db"))
{
    var col = db.GetCollection<Customer>("customers");
    col.Insert(new Customer { Name = "John" });
}
```

### AOT-Compatible Code (With Source Generator)
```csharp
using(var db = new LiteDatabase(@"MyData.db"))
{
    var mapper = BsonMapper.CreateForAOT();
    mapper.RegisterType<Customer>(); // Or use [BsonDocument] attribute
    
    var col = db.GetCollection<Customer>("customers");
    col.Insert(new Customer { Name = "John" });
}
```

### Automatic with Source Generator
```csharp
// Add attribute to enable source generator
[BsonDocument]
public partial class Customer
{
    public int Id { get; set; }
    public string Name { get; set; }
}
```

---

## Breaking Changes

The following breaking changes may be necessary:

1. **Minimum .NET Version**: Require .NET 8.0+ for AOT support
2. **API Changes**: New initialization methods for AOT mode
3. **Type Registration**: Explicit type registration may be required
4. **Removed Features**: Some dynamic features may not work in AOT mode

---

## Risks and Mitigations

### Risk 1: Large Refactoring Scope
**Mitigation**: Phased approach with backward compatibility

### Risk 2: Breaking Changes
**Mitigation**: Versioning strategy (v6.0 for AOT support)

### Risk 3: Performance Regression
**Mitigation**: Comprehensive benchmarking before release

### Risk 4: Incomplete AOT Coverage
**Mitigation**: Clear documentation of limitations

---

## Success Criteria

1. ✅ LiteDB compiles with `<PublishAot>true</PublishAot>`
2. ✅ No AOT warnings or errors in core functionality
3. ✅ All unit tests pass in AOT mode
4. ✅ Performance benchmarks show improvement
5. ✅ Sample applications work without issues
6. ✅ Documentation is complete and clear
7. ✅ Backward compatibility maintained (or clearly versioned)

---

## References

### Microsoft Documentation
- [Native AOT Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Prepare .NET libraries for trimming](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)
- [Introduction to AOT warnings](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings)
- [Source Generators](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)

### Community Examples
- Entity Framework Core AOT implementation
- System.Text.Json source generator
- ASP.NET Core Minimal APIs with AOT

---

## Conclusion

Making LiteDB AOT-compatible is a significant undertaking that requires:

1. **Major refactoring** of the mapping system
2. **Source generators** for compile-time code generation
3. **Dual implementation** paths (AOT and reflection)
4. **Comprehensive testing** and validation
5. **Clear documentation** and migration guides

**Estimated Timeline**: 16 weeks (4 months) for full implementation

**Recommended Approach**: Hybrid approach maintaining backward compatibility while adding AOT support for new applications.

**Next Steps**:
1. Community feedback on approach
2. Create GitHub issue for tracking
3. Begin Phase 1: Add diagnostic attributes
4. Set up source generator infrastructure
5. Incremental implementation with regular previews

---

*Document Version: 1.0*  
*Last Updated: 2026-01-28*  
*Author: GitHub Copilot Analysis*
