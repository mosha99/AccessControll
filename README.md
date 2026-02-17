# 🔐 AccessControll — سیستم کنترل دسترسی هوشمند

یک سیستم کامل **کنترل دسترسی به درها** با معماری Clean Architecture بر پایه:
- **ASP.NET Core 8 Web API** با MediatR (CQRS)
- **OpenIddict** برای مدیریت توکن OAuth2
- **Identity + JWT** برای احراز هویت
- **Blazor WebAssembly** برای رابط کاربری
- **SignalR** برای اعلان‌های Real-time

---

## 📁 ساختار پروژه

```
MediaR/
├── src/
│   ├── AccessControll.Domain/           ← Entities, Enums, Interfaces
│   ├── AccessControll.Application/      ← CQRS Commands/Queries, Handlers
│   ├── AccessControll.Infrastructure/   ← EF Core, Repositories, JWT Service
│   ├── AccessControll.API/              ← ASP.NET Core Controllers, SignalR Hub
│   └── AccessControll.Blazor/           ← Blazor WASM UI
```

---

## 🚀 راه‌اندازی

### پیش‌نیازها
- .NET 8 SDK
- SQL Server (LocalDB یا کامل)

### ۱. تنظیم Connection String

در `AccessControll.API/appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MediaRAccessControl;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "JwtSettings": {
    "SecretKey": "YOUR_STRONG_SECRET_KEY_MIN_32_CHARS",
    "Issuer": "AccessControll.API",
    "Audience": "MediaR.Clients",
    "ExpireMinutes": "60"
  }
}
```

### ۲. Migration دیتابیس

```bash
cd src/AccessControll.Infrastructure
dotnet ef database update --startup-project ../AccessControll.API
```

یا برای ایجاد migration جدید:
```bash
dotnet ef migrations add InitialCreate --startup-project ../AccessControll.API
dotnet ef database update --startup-project ../AccessControll.API
```

### ۳. اجرای API

```bash
cd src/AccessControll.API
dotnet run
# Swagger: https://localhost:7000/swagger
```

### ۴. اجرای Blazor UI

```bash
cd src/AccessControll.Blazor
dotnet run
# UI: https://localhost:7001
```

---

## 🔑 ویژگی‌های اصلی

### احراز هویت (Auth)
| Endpoint | توضیح |
|----------|-------|
| `POST /api/auth/login` | ورود با email/password (+2FA) |
| `POST /api/auth/logout` | خروج |
| `POST /api/auth/2fa/setup` | تنظیم 2FA (TOTP) |
| `POST /api/auth/2fa/verify` | تأیید و فعال‌سازی 2FA |
| `POST /api/auth/2fa/disable` | غیرفعال کردن 2FA |

### مدیریت درها
| Endpoint | نقش مورد نیاز | توضیح |
|----------|--------------|-------|
| `GET /api/doors` | Admin, DoorManager | لیست درها |
| `POST /api/doors` | Admin | ایجاد در جدید |
| `PUT /api/doors/{id}` | Admin | ویرایش در |
| `DELETE /api/doors/{id}` | Admin | حذف در |
| `POST /api/doors/{id}/control` | همه | **قفل/باز کردن در** |
| `GET /api/doors/logs` | Admin, DoorManager | لاگ دسترسی‌ها |
| `POST /api/doors/{id}/permissions` | Admin | اعطای دسترسی |
| `DELETE /api/doors/{id}/permissions/{userId}` | Admin | لغو دسترسی |

### مدیریت کاربران
| Endpoint | توضیح |
|----------|-------|
| `GET /api/users` | لیست کاربران |
| `POST /api/users` | ایجاد کاربر |
| `PUT /api/users/{id}` | ویرایش کاربر |
| `DELETE /api/users/{id}` | حذف کاربر |
| `POST /api/users/{id}/toggle-active` | فعال/غیرفعال کردن |

### نقش‌های داینامیک
| Endpoint | توضیح |
|----------|-------|
| `GET /api/roles` | لیست نقش‌ها |
| `POST /api/roles` | **ایجاد نقش جدید** |
| `DELETE /api/roles/{id}` | حذف نقش |
| `POST /api/roles/assign` | اختصاص نقش به کاربر |
| `POST /api/roles/remove` | حذف نقش از کاربر |

---

## 🎭 نقش‌های پیش‌فرض

| نقش | دسترسی |
|-----|--------|
| `Admin` | کامل‌ترین دسترسی — مدیریت کاربران، درها، نقش‌ها |
| `DoorManager` | مشاهده و کنترل درها، مشاهده لاگ‌ها |
| `User` | کنترل درهایی که دسترسی دارد |

> نقش‌های جدید از طریق پنل ادمین Blazor قابل ایجاد هستند.

---

## 🔒 سیستم لاک و کنترل دسترسی

هر بار که کاربری در را باز یا قفل می‌کند، سیستم موارد زیر را بررسی می‌کند:

1. **در فعال است؟** — اگر غیرفعال، رد می‌شود
2. **کاربر دسترسی دارد؟** — بر اساس `UserDoorPermission`
3. **نوع دسترسی** — آیا اجازه باز کردن دارد؟
4. **ساعت مجاز** — از `AllowedFromTime` تا `AllowedToTime`
5. **ثبت لاگ** — هر تلاش (موفق یا ناموفق) ثبت می‌شود

---

## 📡 Real-time با SignalR

```javascript
// از Blazor UI یا هر کلاینت دیگر
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/door", { accessTokenFactory: () => getToken() })
    .build();

connection.on("DoorStatusChanged", (status) => {
    console.log(`در ${status.doorId} توسط ${status.changedBy} ${status.isLocked ? 'قفل' : 'باز'} شد`);
});
```

---

## 🔐 Two-Factor Authentication (TOTP)

سیستم از **TOTP** (Time-based One-Time Password) استفاده می‌کند:
- سازگار با **Google Authenticator**, **Authy**, **Microsoft Authenticator**
- پس از فعال‌سازی، در هر ورود کد ۶ رقمی درخواست می‌شود

---

## 📊 لاگ دسترسی

هر تراکنش شامل:
- **چه کاربری** (UserId, FullName)
- **کدام در** (DoorId, DoorName)
- **چه ساعتی** (AccessedAt - UTC)
- **چه عملیاتی** (Open, Close, Lock, Unlock, ForceOpen)
- **نتیجه** (Success, Denied, NoPermission, OutsideAllowedHours, ...)
- **آدرس IP**

---

## 🖥️ رابط کاربری Blazor

| صفحه | آدرس | توضیح |
|------|------|-------|
| ورود | `/login` | ورود با 2FA |
| داشبورد | `/` | آمار، کنترل سریع درها، لاگ اخیر |
| درها | `/doors` | مدیریت و کنترل درها |
| لاگ‌ها | `/logs` | تاریخچه کامل با فیلتر |
| کاربران | `/users` | فقط Admin |
| نقش‌ها | `/roles` | فقط Admin — مدیریت داینامیک |

---

## 🛡️ امنیت

- رمز عبور حداقل ۸ کاراکتر، با عدد، حرف بزرگ و کاراکتر خاص
- قفل حساب پس از ۵ تلاش ناموفق (۱۵ دقیقه)
- JWT با TTL قابل تنظیم
- CORS محدود به origin مشخص
- تمام endpoint ها نیاز به احراز هویت دارند
