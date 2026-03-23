SELECT 
    mi."InstallOrder" as "#",
    m."Name" as "Module",
    m."Version",
    mi."InstalledBy",
    mi."InstalledAt",
    mi."EntityCount",
    mi."TypeCount",
    mi."EnumCount",
    mi."ServiceCount",
    mi."RuleCount"
FROM modules m 
JOIN module_installations mi ON m."Id" = mi."ModuleId" 
ORDER BY mi."InstallOrder";
