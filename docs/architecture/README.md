# Architecture & Testing Documentation

This directory contains critical architecture and testing documentation for the BMMDL platform.

> **Main Architecture Document**: For a comprehensive system overview, see [`/ARCHITECTURAL DESIGN.md`](../../ARCHITECTURAL%20DESIGN.md) at the project root.

---

## 📚 Documentation Index

### **🔧 Lazy Loading Refactoring**

Major architectural change implementing lazy loading for MetaModelCacheManager to enable "API-only" test setup.

1. **[metamodel_cache_lazy_loading_refactoring.md](metamodel_cache_lazy_loading_refactoring.md)** ⭐⭐⭐⭐⭐
   - Complete refactoring summary
   - Problem, solution, benefits
   - Implementation details

2. **[metamodel_cache_eager_loading_problem.md](metamodel_cache_eager_loading_problem.md)** ⭐⭐⭐⭐⭐
   - Deep analysis of the eager loading anti-pattern
   - Why it caused E2E test failures
   - Proposed Lazy<T> solution

3. **[materialized_view_service_lazy_loading_fix.md](materialized_view_service_lazy_loading_fix.md)** ⭐⭐⭐⭐
   - Fix for MaterializedViewRefreshService
   - Dependency injection pattern for IHostedService
   - Timing analysis

4. **[schema_creation_strategy_analysis.md](schema_creation_strategy_analysis.md)** ⭐⭐⭐⭐⭐
   - Schema initialization strategy
   - EnsureCreated vs Migrate comparison
   - "First-Module Scope" safety principle

5. **[explicit_api_only_design_pattern.md](explicit_api_only_design_pattern.md)** ⭐⭐⭐⭐⭐
   - API-only design philosophy
   - No direct SQL in test fixtures
   - Security considerations

---

### **🆕 BMMDL.Tests.New Project**

New test project for experimental E2E tests with proper fixture setup.

6. **[e2e_fixture_comparison.md](e2e_fixture_comparison.md)** ⭐⭐⭐⭐⭐
   - Comparison of E2EFixture, E2EStep1Fixture, E2EStep2Fixture
   - Key differences and when to use each
   - Feature matrix

7. **[tests_new_template_enforcement.md](tests_new_template_enforcement.md)** ⭐⭐⭐⭐⭐
   - Template enforcement strategy
   - AI instructions for creating tests
   - Checklist for verification

8. **[e2e_fixture_legacy_usage.md](e2e_fixture_legacy_usage.md)** ⭐⭐⭐⭐
   - Tests still using E2EFixture (legacy)
   - Migration strategy
   - Priority matrix

---

### **🧪 E2E Test Infrastructure**

E2E test infrastructure improvements and standardization.

9. **[e2e_setup_fixture_analysis.md](e2e_setup_fixture_analysis.md)** ⭐⭐⭐⭐
   - Deep analysis of E2ESetupFixture
   - Bootstrap flow and patterns
   - Issues and recommendations

10. **[registry_driven_schema_init_fix.md](registry_driven_schema_init_fix.md)** ⭐⭐⭐⭐
    - Registry-driven schema initialization
    - Fix for RegistryDrivenSchemaInitIntegrationTest
    - Migration from E2E to Unit tests

11. **[e2e_with_database_base_summary.md](e2e_with_database_base_summary.md)** ⭐⭐⭐⭐
    - E2EWithDatabaseTestBase standardization
    - Database test base patterns
    - Cleanup strategies

12. **[database_tests_inventory.md](database_tests_inventory.md)** ⭐⭐⭐⭐
    - Complete inventory of database tests
    - Categorization by type
    - Migration recommendations

---

## 🎯 Quick Reference

### **For New Feature Development**:
1. Read: `tests_new_template_enforcement.md`
2. Use: `E2EStep2Fixture` (see `e2e_fixture_comparison.md`)
3. Follow: Template in `BMMDL.Tests.New/TEST_TEMPLATE.md`

### **For Understanding Lazy Loading**:
1. Start: `metamodel_cache_eager_loading_problem.md`
2. Solution: `metamodel_cache_lazy_loading_refactoring.md`
3. Pattern: `explicit_api_only_design_pattern.md`

### **For E2E Test Setup**:
1. Overview: `e2e_setup_fixture_analysis.md`
2. Fixtures: `e2e_fixture_comparison.md`
3. Database: `e2e_with_database_base_summary.md`

---

## 📊 Documentation Statistics

- **Total Files**: 12
- **Total Size**: ~116KB
- **Topics Covered**: 3 major areas
  - Lazy Loading Refactoring (5 docs)
  - BMMDL.Tests.New Setup (3 docs)
  - E2E Test Infrastructure (4 docs)

---

## 🔄 Maintenance

These documents are snapshots from development sessions. They should be:
- ✅ Kept as historical reference
- ✅ Updated when architecture changes
- ✅ Referenced in code comments when relevant

**Last Updated**: 2026-02-03

---

## 📝 Contributing

When adding new documentation:
1. Follow the existing naming convention: `{topic}_{description}.md`
2. Add entry to this README with ⭐ rating
3. Include creation date and context
4. Use clear, descriptive titles

---

**Note**: These documents were originally in `artifacts/` but moved to `docs/artifacts/` for version control.
