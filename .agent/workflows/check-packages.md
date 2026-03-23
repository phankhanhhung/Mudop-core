---
description: Kiểm tra và cập nhật NuGet packages outdated/vulnerable
---

# Check Outdated & Vulnerable Packages

## 1. Kiểm tra packages outdated
// turbo
```powershell
dotnet list BMMDL.sln package --outdated
```

## 2. Kiểm tra packages có lỗ hổng bảo mật
// turbo
```powershell
dotnet list BMMDL.sln package --vulnerable
```

## 3. Đánh giá kết quả

Xem output và quyết định:
- **Major version updates** (vd: 9.x → 10.x): Cần review breaking changes trước khi update
- **Minor/Patch updates** (vd: 10.0.0 → 10.0.1): Thường an toàn, có thể update ngay
- **Vulnerable packages**: Ưu tiên fix, đặc biệt nếu là direct dependency

## 4. Update packages (nếu cần)

Với từng package cần update:
```powershell
dotnet add <project.csproj> package <PackageName> --version <NewVersion>
```

Hoặc update tất cả trong một project:
```powershell
# Cài tool (1 lần)
dotnet tool install --global dotnet-outdated-tool

# Update tất cả packages
dotnet outdated --upgrade
```

## 5. Verify sau khi update
// turbo
```powershell
dotnet build BMMDL.sln -v q --no-incremental /flp:logfile=artifacts/build.log
```

Kiểm tra build thành công và không có warnings mới.
