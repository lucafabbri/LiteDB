# LiteDB Native AOT Compatibility - Documentation Index

This directory contains comprehensive documentation analyzing the steps required to make LiteDB compatible with .NET Native AOT (Ahead-of-Time) compilation.

## 📚 Documentation Files

### 1. [AOT_QUICK_REFERENCE.md](./AOT_QUICK_REFERENCE.md)
**Target Audience**: Developers using LiteDB  
**Purpose**: Quick reference guide for understanding AOT and checking compatibility

**Contents**:
- What is Native AOT and its benefits
- Current LiteDB AOT status
- AOT blockers explained simply
- How to check AOT compatibility
- Future usage patterns
- FAQ

**Use this if**: You want a quick overview of AOT and how it relates to LiteDB.

---

### 2. [AOT_ROADMAP.md](./AOT_ROADMAP.md)
**Target Audience**: Contributors, maintainers, project managers  
**Purpose**: Actionable implementation roadmap

**Contents**:
- 5-phase implementation plan (16 weeks)
- Critical AOT blockers prioritized
- Detailed tasks and deliverables
- Technical design decisions
- Migration guide preview
- Success metrics and risks

**Use this if**: You want to understand the implementation plan and timeline.

---

### 3. [AOT_COMPATIBILITY_ANALYSIS.md](./AOT_COMPATIBILITY_ANALYSIS.md)
**Target Audience**: Technical architects, senior developers  
**Purpose**: Deep technical analysis

**Contents**:
- Comprehensive analysis of all AOT blockers
- Detailed code examples of problematic patterns
- Complete explanation of required changes
- Alternative implementation approaches
- Performance considerations
- Breaking changes analysis

**Use this if**: You need detailed technical understanding of the challenges and solutions.

---

## 🎯 Quick Start

### For Users
1. Read [AOT_QUICK_REFERENCE.md](./AOT_QUICK_REFERENCE.md) to understand current limitations
2. Current status: **LiteDB does NOT support Native AOT**
3. Estimated AOT support: **LiteDB v6.0** (4-6 months development)

### For Contributors
1. Start with [AOT_ROADMAP.md](./AOT_ROADMAP.md) to understand the implementation plan
2. Review [AOT_COMPATIBILITY_ANALYSIS.md](./AOT_COMPATIBILITY_ANALYSIS.md) for technical details
3. Begin with Phase 1: Foundation (add diagnostic attributes)

### For Maintainers
1. Review all three documents
2. Approve the roadmap and approach
3. Create GitHub tracking issue
4. Set up milestones in project management

---

## 📊 Summary

### Current Status
- **LiteDB Version**: 5.0.x
- **AOT Compatible**: ❌ No
- **Blocker Severity**: Critical
- **Effort Required**: 16 weeks (4 months)

### Key Findings

#### Critical Blockers (Must Fix)
1. **Expression.Compile()** - Dynamic IL generation in mapper
2. **Activator.CreateInstance()** - Dynamic object creation
3. **MakeGenericType()** - Dynamic generic type construction

#### Solution Approach
- **Source Generators** for compile-time code generation
- **Hybrid Implementation** maintaining backward compatibility
- **Opt-in AOT Support** via `[BsonDocument]` attribute

### Timeline Overview
```
Week 1-2:   Foundation        [Add .NET 8, diagnostic attributes]
Week 3-6:   Source Generator  [Create mapper generator]
Week 7-10:  Refactoring       [Update BsonMapper]
Week 11-13: Testing           [AOT test project, benchmarks]
Week 14-16: Documentation     [Guides, samples, release]
```

### Expected Benefits
- ⚡ **2-5x faster** startup time
- 💾 **30-50% less** memory usage
- 📦 **70-80% smaller** binaries
- 🚀 **10-20% faster** runtime performance

---

## 🔧 Technical Highlights

### What Needs to Change
```csharp
// ❌ BEFORE (Not AOT compatible)
var obj = Activator.CreateInstance(type);
var getter = Expression.Lambda<Func<object>>(expr).Compile();
```

```csharp
// ✅ AFTER (AOT compatible with source generator)
[BsonDocument]
public partial class Customer { ... }

// Generator creates:
// - Factory method: CustomerFactory.Create()
// - Property accessors: CustomerAccessors.GetName(), SetName()
```

### User Experience
```csharp
// Minimal code changes required
[BsonDocument] // Just add this attribute
public partial class Customer // and make it partial
{
    public int Id { get; set; }
    public string Name { get; set; }
}

// Usage remains the same
using (var db = new LiteDatabase("data.db"))
{
    var customers = db.GetCollection<Customer>();
    customers.Insert(new Customer { Name = "John" });
}
```

---

## 📈 Implementation Phases

| Phase | Duration | Status | Description |
|-------|----------|--------|-------------|
| **Assessment** | Complete | ✅ Done | Analyze codebase and document findings |
| **Phase 1** | 2 weeks | ⏳ Planned | Add .NET 8 target, diagnostic attributes |
| **Phase 2** | 4 weeks | ⏳ Planned | Create source generator |
| **Phase 3** | 4 weeks | ⏳ Planned | Refactor BsonMapper |
| **Phase 4** | 3 weeks | ⏳ Planned | Testing and validation |
| **Phase 5** | 3 weeks | ⏳ Planned | Documentation and release |

---

## 🎓 Learning Resources

### Understanding AOT
- [Microsoft: Native AOT Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Microsoft: Prepare Libraries for Trimming](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)
- [Microsoft: Source Generators Overview](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)

### Example Implementations
- **System.Text.Json**: Uses source generators for AOT-compatible serialization
- **Entity Framework Core**: Provides compiled models for AOT
- **ASP.NET Core Minimal APIs**: Full AOT support with source generators

---

## 🤝 Contributing

### How to Help
1. **Review Documentation**: Provide feedback on accuracy and completeness
2. **Test Current Blockers**: Validate findings in your environment
3. **Propose Solutions**: Suggest alternative approaches
4. **Implement Features**: Pick up tasks from the roadmap

### Getting Started
1. Read [AOT_ROADMAP.md](./AOT_ROADMAP.md)
2. Comment on GitHub tracking issue (to be created)
3. Join discussions in GitHub Discussions
4. Submit PRs following the roadmap

---

## ⚠️ Important Notes

### For LiteDB Users
- **Current v5.x**: Does NOT support AOT, will NOT be backported
- **Future v6.0**: Will support AOT with minimal code changes
- **Workaround**: Wait for v6.0 or use non-AOT deployment

### For Contributors
- **Breaking Changes**: v6.0 may include breaking changes (properly versioned)
- **Backward Compatibility**: Goal is to maintain compatibility where possible
- **Testing**: AOT support requires comprehensive testing

### For Maintainers
- **Version Strategy**: v6.0 for AOT support (major version bump)
- **Support Matrix**: Continue supporting v5.x for non-AOT scenarios
- **Documentation**: Update all docs to mention AOT requirements

---

## 📝 Document Versions

| Document | Version | Last Updated | Status |
|----------|---------|--------------|--------|
| AOT_QUICK_REFERENCE.md | 1.0 | 2026-01-28 | Complete |
| AOT_ROADMAP.md | 1.0 | 2026-01-28 | Complete |
| AOT_COMPATIBILITY_ANALYSIS.md | 1.0 | 2026-01-28 | Complete |
| AOT_DOCUMENTATION_INDEX.md | 1.0 | 2026-01-28 | Complete |

---

## 📞 Contact & Feedback

- **GitHub Issues**: For bug reports and feature requests
- **GitHub Discussions**: For questions and community discussion
- **Pull Requests**: For code contributions
- **Discord**: [LiteDB Community](https://discord.gg/u8seFBH9Zu)

---

## ✅ Next Steps

### Immediate (Week 1)
1. [ ] Review documentation with core team
2. [ ] Create GitHub tracking issue: "Native AOT Support for v6.0"
3. [ ] Set up project milestones
4. [ ] Announce to community (Discord, GitHub Discussions)

### Short-term (Month 1)
1. [ ] Begin Phase 1: Foundation implementation
2. [ ] Add .NET 8 target framework
3. [ ] Add diagnostic attributes
4. [ ] Set up CI/CD for AOT builds

### Long-term (Months 2-4)
1. [ ] Implement source generator
2. [ ] Refactor mapper system
3. [ ] Comprehensive testing
4. [ ] Release v6.0-preview.1

---

*This analysis was created on 2026-01-28 as part of the effort to make LiteDB compatible with .NET Native AOT compilation.*

**Status**: Analysis Complete ✅  
**Next Phase**: Implementation Planning  
**Target Version**: LiteDB v6.0  
**Estimated Completion**: Q2 2026
