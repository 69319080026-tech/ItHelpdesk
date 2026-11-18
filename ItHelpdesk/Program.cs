using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
// บน Azure App Service เก็บไฟล์ DB ไว้ที่ %HOME%\data (เขียนได้และไม่หายตอน deploy ใหม่)
var dbDir = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") is null
    ? "." : Path.Combine(Environment.GetEnvironmentVariable("HOME")!, "data");
Directory.CreateDirectory(dbDir);
builder.Services.AddDbContext<AppDb>(o => o.UseSqlite($"Data Source={Path.Combine(dbDir, "helpdesk.db")}"));
builder.Services.AddScoped<TicketService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---------- ล็อกอินด้วย Cookie ----------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "helpdesk.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        // API ตอบ 401/403 แทนการ redirect ไปหน้า login
        o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireRole(nameof(Role.Admin))));

var app = builder.Build();
app.UseSwagger(); app.UseSwaggerUI();
app.UseDefaultFiles(); app.UseStaticFiles();   // เสิร์ฟหน้าเว็บจาก wwwroot
app.UseAuthentication(); app.UseAuthorization();

using (var scope = app.Services.CreateScope())
    Seed.Run(scope.ServiceProvider.GetRequiredService<AppDb>());

static int Uid(ClaimsPrincipal u) => int.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier)!);

// ---------- Auth (ไม่ต้องล็อกอิน) ----------
app.MapPost("/auth/login", async (HttpContext http, AppDb db, LoginDto d) =>
{
    var user = db.Users.FirstOrDefault(u => u.Username == (d.Username ?? "").Trim().ToLower());
    if (user is null || !Passwords.Verify(d.Password ?? "", user.PasswordHash))
        return Results.BadRequest(new { error = "ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง" });
    var claims = new[] {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Name),
        new Claim(ClaimTypes.Role, user.Role.ToString()) };
    await http.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(user);
});
app.MapPost("/auth/logout", async (HttpContext http) => { await http.SignOutAsync(); return Results.Ok(); });
app.MapGet("/auth/me", (ClaimsPrincipal u, AppDb db) =>
    u.Identity?.IsAuthenticated == true && db.Users.Find(Uid(u)) is { } me ? Results.Ok(me) : Results.Unauthorized());

// ---------- ทุก API ด้านล่างต้องล็อกอินก่อน ----------
var api = app.MapGroup("").RequireAuthorization();

api.MapGet("/categories", (AppDb db) => db.Categories.ToList());
api.MapPost("/categories", (AppDb db, Category c) =>
{
    if (string.IsNullOrWhiteSpace(c.Name)) return Results.BadRequest(new { error = "ต้องระบุชื่อประเภท" });
    c.Id = 0; db.Categories.Add(c); db.SaveChanges(); return Results.Created($"/categories/{c.Id}", c);
}).RequireAuthorization("Admin");
api.MapPatch("/categories/{id}/toggle", (AppDb db, int id) =>
{
    var c = db.Categories.Find(id); if (c is null) return Results.NotFound();
    c.IsActive = !c.IsActive; db.SaveChanges(); return Results.Ok(c);
}).RequireAuthorization("Admin");
api.MapGet("/users", (AppDb db) => db.Users.ToList());

// ---------- Tickets (เห็นเฉพาะที่มีสิทธิ์) ----------
api.MapGet("/tickets", (ClaimsPrincipal u, TicketService s, TicketStatus? status, int? categoryId) =>
{
    var q = s.VisibleTo(Uid(u));
    if (status != null) q = q.Where(t => t.Status == status);
    if (categoryId != null) q = q.Where(t => t.CategoryId == categoryId);
    return q.OrderByDescending(t => t.CreatedAt).ToList();
});

api.MapGet("/tickets/{id}", (ClaimsPrincipal u, AppDb db, TicketService s, int id) =>
{
    var viewer = db.Users.Find(Uid(u))!;
    if (!s.VisibleTo(viewer.Id).Any(x => x.Id == id)) return Results.NotFound(new { error = "ไม่พบใบแจ้ง หรือไม่มีสิทธิ์ดู" });
    var t = db.Tickets.Include(x => x.Category).Include(x => x.Assignee).Include(x => x.Reporter)
        .Include(x => x.History).ThenInclude(h => h.ChangedBy)
        .First(x => x.Id == id);
    return Results.Ok(new
    {
        ticket = t,
        allowedNext = StatusRules.NextFor(t, viewer),
        canAssign = viewer.Role == Role.Admin,
        history = t.History.OrderBy(h => h.ChangedAt)
    });
});

// userId มาจาก cookie เสมอ — ค่า userId ที่ส่งมาใน body ถูกแทนที่ จึงปลอมตัวเป็นคนอื่นไม่ได้
api.MapPost("/tickets", (ClaimsPrincipal u, TicketService s, CreateTicketDto d) => Run(() => s.Create(d with { UserId = Uid(u) })));
api.MapPatch("/tickets/{id}/assign", (ClaimsPrincipal u, TicketService s, int id, AssignDto d) => Run(() => s.Assign(id, d.AssigneeId, Uid(u))));
api.MapPatch("/tickets/{id}/status", (ClaimsPrincipal u, TicketService s, int id, ChangeStatusDto d) => Run(() => s.ChangeStatus(id, d.ToStatus, Uid(u), d.Note)));

app.Run();

static IResult Run(Func<object> f)
{
    try { return Results.Ok(f()); }
    catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); }
    catch (InvalidOperationException e) { return Results.BadRequest(new { error = e.Message }); }
}

// ================= Models =================
public enum TicketStatus { Open, Assigned, InProgress, Resolved, Closed, Cancelled }
public enum Role { Staff, Technician, Admin }

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public Role Role { get; set; }
    [JsonIgnore] public string PasswordHash { get; set; } = "";
}
public class Category { public int Id { get; set; } public string Name { get; set; } = ""; public bool IsActive { get; set; } = true; }

public class Ticket
{
    public int Id { get; set; }
    public string TicketNo { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Priority { get; set; } = "Medium";
    public int CategoryId { get; set; } public Category? Category { get; set; }
    public int ReporterId { get; set; } public User? Reporter { get; set; }
    public int? AssigneeId { get; set; } public User? Assignee { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<StatusHistory> History { get; set; } = new();
}

public class StatusHistory
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public TicketStatus? FromStatus { get; set; }   // null = สร้างใหม่
    public TicketStatus ToStatus { get; set; }
    public int ChangedById { get; set; } public User? ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? Note { get; set; }
}

public record CreateTicketDto(string Title, string Description, int CategoryId, string Priority, int UserId);
public record AssignDto(int AssigneeId, int UserId);
public record ChangeStatusDto(TicketStatus ToStatus, int UserId, string? Note);
public record LoginDto(string? Username, string? Password);

// ================= DB =================
public class AppDb(DbContextOptions<AppDb> o) : DbContext(o)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<StatusHistory> StatusHistories => Set<StatusHistory>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Ticket>().Property(t => t.Status).HasConversion<string>();
        b.Entity<StatusHistory>().Property(h => h.ToStatus).HasConversion<string>();
        b.Entity<StatusHistory>().Property(h => h.FromStatus).HasConversion<string>();
        b.Entity<Ticket>().HasIndex(t => t.TicketNo).IsUnique();
        b.Entity<Ticket>().HasOne(t => t.Reporter).WithMany().HasForeignKey(t => t.ReporterId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Ticket>().HasOne(t => t.Assignee).WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<User>().HasIndex(u => u.Username).IsUnique();
    }
}

public static class Seed
{
    // บัญชีทดสอบ (แสดงในหน้า login และ README)
    public static readonly (string User, string Pass, string Name, Role Role)[] Accounts =
    {
        ("somchai", "1234",      "สมชาย (พนักงาน)", Role.Staff),
        ("wichai",  "1234",      "วิชัย (ช่าง IT)",  Role.Technician),
        ("admin",   "admin1234", "สุดา (Admin)",    Role.Admin),
        ("somying", "1234",      "สมหญิง (พนักงาน)", Role.Staff),
        ("prasit",  "1234",      "ประสิทธิ์ (ช่าง IT)", Role.Technician),
    };

    public static void Run(AppDb db)
    {
        db.Database.EnsureCreated();
        try { _ = db.Users.Select(u => u.Username).FirstOrDefault(); }
        catch { db.Database.EnsureDeleted(); db.Database.EnsureCreated(); }   // DB รุ่นเก่ายังไม่มีคอลัมน์ล็อกอิน → สร้างใหม่

        if (db.Users.Any()) return;
        foreach (var a in Accounts)   // เพิ่มทีละคนเพื่อให้ Id เรียงตามลำดับ (1=somchai, 2=wichai, 3=admin)
        {
            db.Users.Add(new User { Username = a.User, Name = a.Name, Role = a.Role, PasswordHash = Passwords.Hash(a.Pass) });
            db.SaveChanges();
        }
        db.Categories.AddRange(
            new Category { Name = "Hardware" }, new Category { Name = "Software" },
            new Category { Name = "Network" }, new Category { Name = "Account/Password" });
        db.SaveChanges();
    }
}

// เก็บรหัสผ่านแบบ PBKDF2 + salt (ไม่เก็บรหัสจริง)
public static class Passwords
{
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

// ================= Business rules =================
public static class StatusRules
{
    static readonly Dictionary<TicketStatus, TicketStatus[]> Map = new()
    {
        [TicketStatus.Open]       = [TicketStatus.Assigned, TicketStatus.Cancelled],
        [TicketStatus.Assigned]   = [TicketStatus.InProgress, TicketStatus.Cancelled],
        [TicketStatus.InProgress] = [TicketStatus.Resolved, TicketStatus.Cancelled],
        [TicketStatus.Resolved]   = [TicketStatus.Closed, TicketStatus.InProgress],
        [TicketStatus.Closed]     = [],
        [TicketStatus.Cancelled]  = [],
    };
    public static TicketStatus[] Next(TicketStatus s) => Map[s];
    public static bool CanMove(TicketStatus from, TicketStatus to) => Map[from].Contains(to);

    // สิทธิ์ตามบทบาท: Admin ทำได้ทุกอย่าง, ช่างทำได้เฉพาะงานที่ตัวเองรับ, ผู้แจ้งปิด/เปิดซ้ำ/ยกเลิกใบของตัวเอง
    public static bool CanUserDo(Ticket t, User u, TicketStatus to)
    {
        if (u.Role == Role.Admin) return true;
        bool isAssignee = t.AssigneeId == u.Id;
        bool isReporter = t.ReporterId == u.Id;
        return to switch
        {
            TicketStatus.Assigned   => false,                                   // มอบหมายได้เฉพาะ Admin
            TicketStatus.InProgress => t.Status == TicketStatus.Resolved ? isReporter : isAssignee,
            TicketStatus.Resolved   => isAssignee,
            TicketStatus.Closed     => isReporter,
            TicketStatus.Cancelled  => isReporter,
            _ => false
        };
    }

    public static TicketStatus[] NextFor(Ticket t, User? u) =>
        Map[t.Status].Where(s => s != TicketStatus.Assigned)          // Assigned ทำผ่านปุ่ม "มอบหมาย"
                     .Where(s => u is null || CanUserDo(t, u, s)).ToArray();
}

public class TicketService(AppDb db)
{
    Func<DateTime> Now = () => DateTime.Now;

    // พนักงานเห็นใบของตัวเอง, ช่างเห็นงานที่ได้รับมอบหมาย, Admin เห็นทั้งหมด
    public IQueryable<Ticket> VisibleTo(int userId)
    {
        var u = GetUser(userId);
        var q = db.Tickets.Include(t => t.Category).Include(t => t.Assignee).Include(t => t.Reporter).AsQueryable();
        return u.Role switch
        {
            Role.Admin      => q,
            Role.Technician => q.Where(t => t.AssigneeId == u.Id),
            _               => q.Where(t => t.ReporterId == u.Id),
        };
    }

    User GetUser(int id) => db.Users.Find(id) ?? throw new KeyNotFoundException($"ไม่พบผู้ใช้ {id}");
    Ticket GetTicket(int id) => db.Tickets.Find(id) ?? throw new KeyNotFoundException($"ไม่พบใบแจ้ง {id}");

    public Ticket Create(CreateTicketDto d)
    {
        var user = GetUser(d.UserId);
        if (string.IsNullOrWhiteSpace(d.Title)) throw new InvalidOperationException("ต้องระบุหัวข้อ");
        if (db.Categories.Find(d.CategoryId) is not { IsActive: true }) throw new InvalidOperationException("ประเภทปัญหาไม่ถูกต้อง");

        var now = Now();
        var t = new Ticket
        {
            Title = d.Title, Description = d.Description, CategoryId = d.CategoryId,
            Priority = d.Priority, ReporterId = user.Id, Status = TicketStatus.Open,
            CreatedAt = now, UpdatedAt = now,
            TicketNo = $"IT-{now:yyyyMMdd}-{db.Tickets.Count() + 1:D4}"
        };
        t.History.Add(new StatusHistory { FromStatus = null, ToStatus = TicketStatus.Open, ChangedById = user.Id, ChangedAt = now, Note = "สร้างใบแจ้ง" });
        db.Tickets.Add(t);
        db.SaveChanges();
        return t;
    }

    public Ticket Assign(int ticketId, int assigneeId, int userId)
    {
        var t = GetTicket(ticketId);
        if (GetUser(userId).Role != Role.Admin) throw new InvalidOperationException("มอบหมายงานได้เฉพาะ Admin");
        var tech = GetUser(assigneeId);
        if (tech.Role == Role.Staff) throw new InvalidOperationException("ผู้รับงานต้องเป็นช่าง IT หรือ Admin");
        t.AssigneeId = tech.Id;
        if (t.Status == TicketStatus.Open)
            return ChangeStatus(ticketId, TicketStatus.Assigned, userId, $"มอบหมายให้ {tech.Name}");
        if (t.Status is TicketStatus.Closed or TicketStatus.Cancelled)
            throw new InvalidOperationException("ใบแจ้งปิดแล้ว เปลี่ยนผู้รับไม่ได้");
        t.UpdatedAt = Now();
        db.SaveChanges();
        return t;
    }

    public Ticket ChangeStatus(int ticketId, TicketStatus to, int userId, string? note)
    {
        var user = GetUser(userId);
        var t = GetTicket(ticketId);
        if (!StatusRules.CanMove(t.Status, to))
            throw new InvalidOperationException($"เปลี่ยนสถานะจาก {t.Status} เป็น {to} ไม่ได้");
        if (to == TicketStatus.Assigned && t.AssigneeId is null)
            throw new InvalidOperationException("ต้องระบุผู้รับงานก่อน");
        if (!StatusRules.CanUserDo(t, user, to))
            throw new InvalidOperationException($"{user.Name} ไม่มีสิทธิ์เปลี่ยนสถานะเป็น {to}");

        var now = Now();
        db.StatusHistories.Add(new StatusHistory
        {
            TicketId = t.Id, FromStatus = t.Status, ToStatus = to,
            ChangedById = user.Id, ChangedAt = now, Note = note
        });
        t.Status = to;
        t.UpdatedAt = now;
        db.SaveChanges();   // บันทึกสถานะ + ประวัติใน transaction เดียว
        return t;
    }
}
