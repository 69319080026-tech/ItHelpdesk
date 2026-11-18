# 🛠️ IT Helpdesk — ระบบรับแจ้งปัญหาด้าน IT

ระบบสำหรับแจ้งปัญหาด้าน IT มอบหมายผู้รับงาน และติดตามความคืบหน้า
การเปลี่ยนสถานะต้องเป็นไปตามเส้นทางที่กำหนด และทุกครั้งที่เปลี่ยนจะบันทึก **ผู้เปลี่ยน + เวลา** ไว้เสมอ
ใช้งานได้ทั้งบนคอมพิวเตอร์และโทรศัพท์ (ติดตั้งเป็นแอปบนหน้าจอโฮมได้ — PWA)

| | ลิงก์ |
|---|---|
| 🌐 เว็บที่ deploy แล้ว | **https://ithelpdesk-3xp8.onrender.com** |
| 📘 API Docs (Swagger) | https://ithelpdesk-3xp8.onrender.com/swagger |
| 🗄️ สคริปต์ฐานข้อมูล | [`database.sql`](database.sql) |

> เว็บ deploy บน Render แบบฟรี — ถ้าไม่มีคนใช้ 15 นาทีเซิร์ฟเวอร์จะหลับ การเปิดครั้งแรกอาจรอประมาณ 1 นาที
> และข้อมูลจะกลับเป็นค่าเริ่มต้นเมื่อเซิร์ฟเวอร์รีสตาร์ต (บัญชีทดสอบยังอยู่ครบ)

---

## 🔑 บัญชีทดสอบ

ระบบสร้างบัญชีให้อัตโนมัติ ไม่ต้องสร้างเอง — ในหน้าเข้าสู่ระบบมีปุ่มบัญชีทดสอบ กดแล้วเข้าได้ทันที

| ชื่อผู้ใช้ | รหัสผ่าน | ชื่อ | บทบาท |
|---|---|---|---|
| `admin` | `admin1234` | สุดา | 🛡️ Admin |
| `somchai` | `1234` | สมชาย | 👤 พนักงาน |
| `somying` | `1234` | สมหญิง | 👤 พนักงาน |
| `wichai` | `1234` | วิชัย | 🔧 ช่าง IT |
| `prasit` | `1234` | ประสิทธิ์ | 🔧 ช่าง IT |

### สิทธิ์การเข้าถึงตามบทบาท

| | พนักงาน | ช่าง IT | Admin |
|---|:---:|:---:|:---:|
| แจ้งปัญหาใหม่ | ✅ | ✅ | ✅ |
| ใบแจ้งที่มองเห็น | เฉพาะของตัวเอง | เฉพาะงานที่ได้รับมอบหมาย | ทั้งหมด |
| มอบหมายผู้รับงาน | ❌ | ❌ | ✅ |
| กด "กำลังดำเนินการ" / "แก้ไขเสร็จ" | ❌ | ✅ (งานของตัวเอง) | ✅ |
| ปิดงาน / เปิดงานซ้ำ / ยกเลิก | ✅ (ใบของตัวเอง) | ❌ | ✅ |
| จัดการประเภทปัญหา | ❌ | ❌ | ✅ |

---

## 🔄 เส้นทางสถานะที่อนุญาต

```
รอรับเรื่อง ──► มอบหมายแล้ว ──► กำลังดำเนินการ ──► แก้ไขเสร็จ ──► ปิดงาน
  (Open)        (Assigned)       (InProgress)       (Resolved)     (Closed)
    │               │                 │                 │
    └───────────────┴─────────────────┴──► ยกเลิก        └──► กำลังดำเนินการ (เปิดงานซ้ำ)
                                      (Cancelled)
```

- เปลี่ยนนอกเส้นทาง (เช่น ข้ามขั้น หรือเปลี่ยนหลังปิดงาน) → ระบบปฏิเสธ และ **ไม่บันทึกประวัติ**
- จะเป็น "มอบหมายแล้ว" ได้ต้องมีผู้รับงานก่อน
- ทุกการเปลี่ยนบันทึกลงตาราง `StatusHistories`: สถานะเดิม → สถานะใหม่, ผู้เปลี่ยน, เวลา, หมายเหตุ

---

## 🧱 เทคโนโลยี

| ส่วน | เทคโนโลยี |
|---|---|
| Front end | HTML / CSS / JavaScript (Responsive) + PWA (manifest, service worker) |
| Back end | C# — ASP.NET Core 8 Minimal API |
| Database | SQLite ผ่าน Entity Framework Core |
| Authentication | Cookie (HttpOnly) + รหัสผ่านแบบ PBKDF2-SHA256 + salt |
| Test | xUnit (15 tests) |
| Deploy | Docker บน Render |

## 🗄️ ฐานข้อมูล

| ตาราง | เก็บอะไร |
|---|---|
| `Categories` | ประเภทปัญหา (Hardware, Software, Network, Account/Password) |
| `Users` | ผู้ใช้ บทบาท และรหัสผ่านที่เข้ารหัสแล้ว |
| `Tickets` | ใบแจ้งปัญหา: เลขที่, หัวข้อ, รายละเอียด, ประเภท, ความสำคัญ, ผู้แจ้ง, ผู้รับ, สถานะ |
| `StatusHistories` | ประวัติการเปลี่ยนสถานะ: สถานะเดิม/ใหม่, ผู้เปลี่ยน, เวลา, หมายเหตุ |

- โปรแกรมสร้างฐานข้อมูลและข้อมูลตั้งต้นให้อัตโนมัติตอนรันครั้งแรก (`ItHelpdesk/helpdesk.db`)
- [`database.sql`](database.sql) คือสคริปต์โครงสร้างตาราง + บัญชีทดสอบ + ใบแจ้งตัวอย่าง + ตัวอย่าง query
  ```
  sqlite3 helpdesk.db < database.sql
  ```

---

## ▶️ วิธีรันในเครื่อง

**ต้องมี:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows: `winget install Microsoft.DotNet.SDK.8`)

```bash
git clone https://github.com/69319080026-tech/ItHelpdesk.git
cd ItHelpdesk

# รันเว็บ → เปิด http://localhost:5000
dotnet run --project ItHelpdesk

# รันเทสต์ (เปิด Terminal อีกหน้าต่าง) → ควรได้ Passed 15/15
dotnet test ItHelpdesk.sln
```

หรือเปิด `ItHelpdesk.sln` ด้วย Visual Studio 2022 แล้วกด **F5**

**ลืมรหัสผ่าน / อยากล้างข้อมูล:** หยุดเว็บ แล้วลบ `ItHelpdesk/helpdesk.db*` จากนั้นรันใหม่ บัญชีทดสอบจะถูกสร้างกลับมา

### 📱 เปิดบนโทรศัพท์
- ใช้ลิงก์ที่ deploy แล้ว: https://ithelpdesk-3xp8.onrender.com
- หรือรันในเครื่องแล้วเปิดผ่าน Cloudflare Tunnel: `cloudflared tunnel --url http://localhost:5000`
- ติดตั้งเป็นแอป: Android (Chrome) → ⋮ → **ติดตั้งแอป** / iPhone (Safari) → แชร์ → **เพิ่มไปยังหน้าจอโฮม**

---

## 🔌 API

ทุก endpoint (ยกเว้น `/auth/*`) ต้องล็อกอินก่อน — ระบบอ่านตัวตนจาก cookie เท่านั้น จึงปลอมเป็นผู้ใช้อื่นไม่ได้

| Method | URL | ใช้ทำ | สิทธิ์ |
|---|---|---|---|
| POST | `/auth/login` | เข้าสู่ระบบ | ทุกคน |
| POST | `/auth/logout` | ออกจากระบบ | ทุกคน |
| GET | `/auth/me` | ข้อมูลผู้ใช้ที่ล็อกอินอยู่ | ล็อกอิน |
| GET | `/tickets?status=&categoryId=` | รายการใบแจ้ง (กรองตามสิทธิ์) | ล็อกอิน |
| GET | `/tickets/{id}` | รายละเอียด + ประวัติ + สถานะที่ไปต่อได้ | ล็อกอิน |
| POST | `/tickets` | สร้างใบแจ้ง | ล็อกอิน |
| PATCH | `/tickets/{id}/assign` | มอบหมายผู้รับงาน | Admin |
| PATCH | `/tickets/{id}/status` | เปลี่ยนสถานะ | ตามบทบาท |
| GET | `/categories` | ประเภทปัญหา | ล็อกอิน |
| POST | `/categories` | เพิ่มประเภท | Admin |
| PATCH | `/categories/{id}/toggle` | เปิด/ปิดใช้งานประเภท | Admin |
| GET | `/users` | รายชื่อผู้ใช้ | ล็อกอิน |

---

## ✅ กรณีทดสอบ (`ItHelpdesk.Tests/TicketTests.cs`)

1. สร้างใบแจ้ง → สถานะเริ่มที่ Open และมีประวัติ 1 แถว
2. เปลี่ยนเป็น Assigned โดยไม่มีผู้รับ → ถูกปฏิเสธ
3. เดินครบเส้นทางจนถึง Closed → สำเร็จ มีประวัติ 5 แถว และผู้เปลี่ยนถูกต้องทุกแถว
4. ข้ามขั้น (Open → Resolved) → ถูกปฏิเสธ และไม่เพิ่มประวัติ
5. Closed → สถานะอื่น → ถูกปฏิเสธ (3 กรณี)
6. ประวัติทุกแถวมีผู้เปลี่ยนและเวลา
7. Resolved → InProgress (เปิดงานซ้ำ) → สำเร็จ
8. มอบหมายงานให้พนักงานทั่วไป → ถูกปฏิเสธ
9. มอบหมายงานได้เฉพาะ Admin
10. พนักงานกด "แก้ไขเสร็จ" ไม่ได้ (ต้องเป็นช่างที่ได้รับมอบหมาย)
11. ช่างปิดงานแทนผู้แจ้งไม่ได้
12. การมองเห็นตามบทบาท: พนักงานเห็นเฉพาะใบของตัวเอง, ช่างเห็นเฉพาะงานที่ได้รับ, Admin เห็นทั้งหมด
13. รหัสผ่านถูก hash และตรวจสอบได้ถูกต้อง

ผลล่าสุด: **Passed 15/15**

---

## 📁 โครงสร้างโปรเจกต์

```
ItHelpdesk/
├─ ItHelpdesk.sln
├─ README.md
├─ database.sql              ← สคริปต์ฐานข้อมูล
├─ Dockerfile                ← สำหรับ deploy (Render)
├─ ItHelpdesk/               ← Back end + Front end
│  ├─ Program.cs             ← API, models, กฎสถานะ, สิทธิ์, ล็อกอิน
│  ├─ ItHelpdesk.csproj
│  └─ wwwroot/
│     ├─ index.html          ← หน้าเว็บทั้งหมด
│     ├─ manifest.json, sw.js, icon.svg   ← PWA
└─ ItHelpdesk.Tests/
   └─ TicketTests.cs         ← เทสต์อัตโนมัติ
```

## ☁️ Deploy บน Render

1. เข้า render.com → Sign in with GitHub
2. New → Web Service → เลือก repository นี้
3. Language: **Docker**, Instance Type: **Free** → Deploy
4. ทุกครั้งที่ push ขึ้น GitHub Render จะ deploy ใหม่ให้อัตโนมัติ
