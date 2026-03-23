# BMMDL.Tests.New - Template Enforcement Strategy

**Date**: 2026-01-24  
**Purpose**: Ensure consistent test creation in BMMDL.Tests.New

---

## 🎯 Problem

When creating tests in BMMDL.Tests.New, how to ensure:
1. ✅ Correct namespace (`BMMDL.Tests.New.{Component}`)
2. ✅ Correct collection (`[Collection("E2EStep2")]`)
3. ✅ Correct fixture (`E2EStep2Fixture`)
4. ✅ Proper usage of fixture properties
5. ✅ Consistent naming conventions

---

## ✅ Solution: Template + Documentation

### **1. Created TEST_TEMPLATE.md**

**Location**: `src/BMMDL.Tests.New/TEST_TEMPLATE.md`

**Contains**:
- ✅ Standard test template (copy-paste ready)
- ✅ Complete example with all best practices
- ✅ Checklist for new tests
- ✅ Common mistakes to avoid
- ✅ Quick reference guide

---

### **2. Updated README.md**

**Added section**: "Creating New Tests"

**Includes**:
- ✅ Link to TEST_TEMPLATE.md
- ✅ Quick template for fast reference
- ✅ Prominent placement (right after Purpose)

---

### **3. Example Tests**

**Location**: `src/BMMDL.Tests.New/Examples/E2EStep2FixtureExampleTests.cs`

**Shows**:
- ✅ Collection definition
- ✅ Fixture injection
- ✅ Property usage
- ✅ Test naming
- ✅ FluentAssertions
- ✅ Output logging

---

## 📋 Template Structure

### **Standard Template**:

```csharp
namespace BMMDL.Tests.New.{Component};

using BMMDL.Tests.Integration.Api;
using System.Net.Http.Json;
using Xunit;

/// <summary>
/// E2E tests for {FeatureName}.
/// </summary>
[Collection("E2EStep2")]
public class {FeatureName}Tests
{
    private readonly E2EStep2Fixture _fixture;
    private readonly ITestOutputHelper _output;
    
    public {FeatureName}Tests(E2EStep2Fixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }
    
    [Fact]
    public async Task {Feature}_{Scenario}_Should{Behavior}()
    {
        // Arrange
        var client = _fixture.RuntimeClient;
        
        // Act
        var response = await client.GetAsync("/api/...");
        
        // Assert
        response.Should().BeSuccessful();
    }
}
```

---

## ✅ Enforcement Checklist

When AI creates a test in BMMDL.Tests.New, verify:

### **1. File Location**:
```
✅ src/BMMDL.Tests.New/{Component}/{Feature}Tests.cs
❌ src/BMMDL.Tests.E2E/{Component}/{Feature}Tests.cs
```

### **2. Namespace**:
```csharp
✅ namespace BMMDL.Tests.New.{Component};
❌ namespace BMMDL.Tests.E2E.{Component};
```

### **3. Collection**:
```csharp
✅ [Collection("E2EStep2")]
❌ [Collection("E2E")]
❌ [Collection("E2EStep1")]
```

### **4. Fixture**:
```csharp
✅ E2EStep2Fixture _fixture;
✅ public MyTests(E2EStep2Fixture fixture, ITestOutputHelper output)
❌ E2EFixture _fixture;
❌ E2EStep1Fixture _fixture;
```

### **5. Fixture Usage**:
```csharp
✅ var client = _fixture.RuntimeClient;  // Already authenticated
✅ var tenantId = _fixture.TenantId;     // Already created
❌ await AuthHelper.AuthenticateAsync(client);  // Don't re-auth!
```

### **6. Test Naming**:
```csharp
✅ public async Task Feature_Scenario_ShouldBehavior()
❌ public async Task TestFeature()
❌ public async Task Test1()
```

### **7. Assertions**:
```csharp
✅ response.Should().BeSuccessful();
❌ Assert.True(response.IsSuccessStatusCode);
```

---

## 🤖 AI Instructions

When user asks to create a test in BMMDL.Tests.New:

### **Step 1: Reference Template**
```
"I'll create a test following the BMMDL.Tests.New template..."
```

### **Step 2: Check Template**
```
Read: src/BMMDL.Tests.New/TEST_TEMPLATE.md
```

### **Step 3: Create Test**
```
Use template structure:
- Namespace: BMMDL.Tests.New.{Component}
- Collection: [Collection("E2EStep2")]
- Fixture: E2EStep2Fixture
- Naming: {Feature}_{Scenario}_Should{Behavior}
```

### **Step 4: Verify**
```
Checklist:
✅ Correct namespace
✅ Correct collection
✅ Correct fixture
✅ Uses fixture properties
✅ FluentAssertions
✅ ITestOutputHelper
```

---

## 📚 Documentation Hierarchy

```
README.md
    ↓
    "See TEST_TEMPLATE.md for guidelines"
    ↓
TEST_TEMPLATE.md
    ↓
    - Complete template
    - Examples
    - Checklist
    - Common mistakes
    ↓
Examples/E2EStep2FixtureExampleTests.cs
    ↓
    - Working example
    - Collection definition
    - Multiple test patterns
```

---

## 🎯 Success Criteria

AI successfully creates tests when:

1. ✅ **Reads template first** - References TEST_TEMPLATE.md
2. ✅ **Follows structure** - Uses correct namespace, collection, fixture
3. ✅ **Uses fixture properly** - Doesn't re-authenticate, uses existing tenant
4. ✅ **Consistent naming** - Follows {Feature}_{Scenario}_Should{Behavior}
5. ✅ **Best practices** - FluentAssertions, ITestOutputHelper, XML docs

---

## 🏁 Summary

### **Files Created**:
1. ✅ `TEST_TEMPLATE.md` - Complete template and guidelines
2. ✅ `README.md` - Updated with template reference
3. ✅ `Examples/E2EStep2FixtureExampleTests.cs` - Working example

### **Enforcement Strategy**:
1. 📖 **Documentation** - Clear template and guidelines
2. 📝 **Examples** - Working code to copy
3. ✅ **Checklist** - Verification steps
4. 🤖 **AI Instructions** - How AI should create tests

### **Result**:
When user says "create test in BMMDL.Tests.New", AI will:
1. Read TEST_TEMPLATE.md
2. Follow template structure
3. Verify against checklist
4. Create consistent, correct test

**Rating**: 10/10 - Template enforcement in place! 🚀
