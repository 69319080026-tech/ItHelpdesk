-- =====================================================================
--  ระบบรับแจ้งปัญหา IT (IT Helpdesk) — สคริปต์ฐานข้อมูล (SQLite)
--  โครงสร้างตรงกับที่ Entity Framework Core สร้างจริงใน helpdesk.db
--
--  หมายเหตุ: โปรแกรมสร้างฐานข้อมูลและข้อมูลตั้งต้นให้อัตโนมัติตอนรันครั้งแรก
--  สคริปต์นี้ใช้สำหรับประกอบรายงาน หรือสร้างฐานข้อมูลเองด้วยมือ:
--      sqlite3 helpdesk.db < database.sql
-- =====================================================================

PRAGMA foreign_keys = ON;

DROP TABLE IF EXISTS "StatusHistories";
DROP TABLE IF EXISTS "Tickets";
DROP TABLE IF EXISTS "Users";
DROP TABLE IF EXISTS "Categories";

-- ---------- 1) ประเภทปัญหา ----------
CREATE TABLE "Categories" (
    "Id"       INTEGER NOT NULL CONSTRAINT "PK_Categories" PRIMARY KEY AUTOINCREMENT,
    "Name"     TEXT    NOT NULL,
    "IsActive" INTEGER NOT NULL                 -- 1 = ใช้งาน, 0 = ปิดใช้งาน
);

-- ---------- 2) ผู้ใช้ ----------
CREATE TABLE "Users" (
    "Id"           INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT,
    "Username"     TEXT    NOT NULL,
    "Name"         TEXT    NOT NULL,
    "Role"         INTEGER NOT NULL,            -- 0 = Staff (พนักงาน), 1 = Technician (ช่าง IT), 2 = Admin
    "PasswordHash" TEXT    NOT NULL             -- PBKDF2-SHA256 100,000 รอบ: base64(salt).base64(hash)
);
CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username");

-- ---------- 3) ใบแจ้งปัญหา ----------
CREATE TABLE "Tickets" (
    "Id"          INTEGER NOT NULL CONSTRAINT "PK_Tickets" PRIMARY KEY AUTOINCREMENT,
    "TicketNo"    TEXT    NOT NULL,             -- เช่น IT-20261118-0001
    "Title"       TEXT    NOT NULL,
    "Description" TEXT    NOT NULL,
    "Priority"    TEXT    NOT NULL,             -- Low, Medium, High
    "CategoryId"  INTEGER NOT NULL,
    "ReporterId"  INTEGER NOT NULL,             -- ผู้แจ้ง
    "AssigneeId"  INTEGER NULL,                 -- ผู้รับงาน (ว่างได้จนกว่าจะมอบหมาย)
    "Status"      TEXT    NOT NULL,             -- Open, Assigned, InProgress, Resolved, Closed, Cancelled
    "CreatedAt"   TEXT    NOT NULL,
    "UpdatedAt"   TEXT    NOT NULL,
    CONSTRAINT "FK_Tickets_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Tickets_Users_AssigneeId"      FOREIGN KEY ("AssigneeId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Tickets_Users_ReporterId"      FOREIGN KEY ("ReporterId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX "IX_Tickets_TicketNo"   ON "Tickets" ("TicketNo");
CREATE INDEX        "IX_Tickets_AssigneeId" ON "Tickets" ("AssigneeId");
CREATE INDEX        "IX_Tickets_CategoryId" ON "Tickets" ("CategoryId");
CREATE INDEX        "IX_Tickets_ReporterId" ON "Tickets" ("ReporterId");

-- ---------- 4) ประวัติสถานะ (เก็บผู้เปลี่ยน + เวลา ทุกครั้ง) ----------
CREATE TABLE "StatusHistories" (
    "Id"          INTEGER NOT NULL CONSTRAINT "PK_StatusHistories" PRIMARY KEY AUTOINCREMENT,
    "TicketId"    INTEGER NOT NULL,
    "FromStatus"  TEXT    NULL,                 -- NULL = สร้างใบแจ้งใหม่
    "ToStatus"    TEXT    NOT NULL,
    "ChangedById" INTEGER NOT NULL,             -- ผู้เปลี่ยน
    "ChangedAt"   TEXT    NOT NULL,             -- เวลาที่เปลี่ยน
    "Note"        TEXT    NULL,
    CONSTRAINT "FK_StatusHistories_Tickets_TicketId"   FOREIGN KEY ("TicketId")    REFERENCES "Tickets" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_StatusHistories_Users_ChangedById"  FOREIGN KEY ("ChangedById") REFERENCES "Users" ("Id") ON DELETE CASCADE
);
CREATE INDEX "IX_StatusHistories_TicketId"    ON "StatusHistories" ("TicketId");
CREATE INDEX "IX_StatusHistories_ChangedById" ON "StatusHistories" ("ChangedById");

-- =====================================================================
--  ข้อมูลตั้งต้น
-- =====================================================================
INSERT INTO "Categories" ("Id","Name","IsActive") VALUES
  (1,'Hardware',1), (2,'Software',1), (3,'Network',1), (4,'Account/Password',1);

-- บัญชีทดสอบ: somchai/1234, wichai/1234, admin/admin1234, somying/1234, prasit/1234
INSERT INTO "Users" ("Id","Username","Name","Role","PasswordHash") VALUES
  (1, 'somchai', 'สมชาย (พนักงาน)', 0, 'q2+qjtGTbKms2MBUSyC20w==.htyLsuenaCIdbmX/YqFKk/4vFirTyDRT8x37h41psis='),
  (2, 'wichai', 'วิชัย (ช่าง IT)', 1, '905GadUKNWQUNla+gtHn2Q==.LM6SNw3ngbH7h4ERj0Qozfsj0MYGTEZKYgYiu4WfaEE='),
  (3, 'admin', 'สุดา (Admin)', 2, 'S93/7vnZAS5ai1fMPH9NMg==.r7LdlW4vaxMk0jXNo99FI6QgqWzJn2/cOB9dSiMLuqg='),
  (4, 'somying', 'สมหญิง (พนักงาน)', 0, 'NSol0EKVqNYzPK/qmpdVtg==.J2CjFXfdwHWHvVYzG4PhtR6FyBYPBIM1Fj85sz4jQ6U='),
  (5, 'prasit', 'ประสิทธิ์ (ช่าง IT)', 1, 'uvya47ibCqfB8W8v0Qx+RA==.RjbA20SvrEg/nK8qy6gNl4IBtmXU24/p7ZvM855jtlM=');

-- ---------- ตัวอย่างใบแจ้ง + ประวัติ ----------
INSERT INTO "Tickets" VALUES
  (1,'IT-20261118-0001','เครื่องพิมพ์ชั้น 2 ไม่ทำงาน','กดพิมพ์แล้วไม่ออก','High',1,1,2,'Closed','2026-11-18 09:00:00','2026-11-18 11:30:00'),
  (2,'IT-20261118-0002','ลืมรหัสผ่านอีเมล','เข้าอีเมลไม่ได้','Medium',4,4,5,'InProgress','2026-11-18 10:15:00','2026-11-18 10:40:00'),
  (3,'IT-20261118-0003','Wi-Fi ห้องประชุมหลุดบ่อย','หลุดทุก 10 นาที','Low',3,1,NULL,'Open','2026-11-18 13:00:00','2026-11-18 13:00:00');

INSERT INTO "StatusHistories" ("TicketId","FromStatus","ToStatus","ChangedById","ChangedAt","Note") VALUES
  (1,NULL,'Open',1,'2026-11-18 09:00:00','สร้างใบแจ้ง'),
  (1,'Open','Assigned',3,'2026-11-18 09:10:00','มอบหมายให้ วิชัย (ช่าง IT)'),
  (1,'Assigned','InProgress',2,'2026-11-18 09:30:00','เริ่มตรวจสอบ'),
  (1,'InProgress','Resolved',2,'2026-11-18 11:00:00','เปลี่ยนตลับหมึก'),
  (1,'Resolved','Closed',1,'2026-11-18 11:30:00','ใช้งานได้แล้ว'),
  (2,NULL,'Open',4,'2026-11-18 10:15:00','สร้างใบแจ้ง'),
  (2,'Open','Assigned',3,'2026-11-18 10:20:00','มอบหมายให้ ประสิทธิ์ (ช่าง IT)'),
  (2,'Assigned','InProgress',5,'2026-11-18 10:40:00',NULL),
  (3,NULL,'Open',1,'2026-11-18 13:00:00','สร้างใบแจ้ง');

-- =====================================================================
--  ตัวอย่าง Query
-- =====================================================================
-- 1) ประวัติสถานะของใบแจ้ง พร้อมผู้เปลี่ยนและเวลา
-- SELECT t.TicketNo, h.FromStatus, h.ToStatus, u.Name AS ChangedBy, h.ChangedAt, h.Note
-- FROM StatusHistories h
-- JOIN Tickets t ON t.Id = h.TicketId
-- JOIN Users   u ON u.Id = h.ChangedById
-- ORDER BY t.Id, h.ChangedAt;

-- 2) จำนวนใบแจ้งแยกตามสถานะ
-- SELECT Status, COUNT(*) AS Total FROM Tickets GROUP BY Status;

-- 3) งานค้างของช่างแต่ละคน
-- SELECT u.Name, COUNT(*) AS OpenJobs
-- FROM Tickets t JOIN Users u ON u.Id = t.AssigneeId
-- WHERE t.Status IN ('Assigned','InProgress')
-- GROUP BY u.Name;

-- 4) จำนวนใบแจ้งแยกตามประเภทปัญหา
-- SELECT c.Name, COUNT(t.Id) AS Total
-- FROM Categories c LEFT JOIN Tickets t ON t.CategoryId = c.Id
-- GROUP BY c.Name;
