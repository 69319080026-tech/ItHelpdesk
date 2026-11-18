using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class TicketTests : IDisposable
{
    readonly SqliteConnection conn = new("Data Source=:memory:");
    readonly AppDb db;
    readonly TicketService svc;
    const int Staff = 1, Tech = 2, Admin = 3;

    public TicketTests()
    {
        conn.Open();
        db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options);
        Seed.Run(db);
        svc = new TicketService(db);
    }
    public void Dispose() { db.Dispose(); conn.Dispose(); }

    Ticket NewTicket() => svc.Create(new("คอมเปิดไม่ติด", "กดปุ่มแล้วเงียบ", 1, "High", Staff));
    List<StatusHistory> Hist(int id) => db.StatusHistories.Where(h => h.TicketId == id).OrderBy(h => h.Id).ToList();

    [Fact] public void Create_StartsOpen_WithOneHistory()
    {
        var t = NewTicket();
        Assert.Equal(TicketStatus.Open, t.Status);
        var h = Assert.Single(Hist(t.Id));
        Assert.Null(h.FromStatus);
        Assert.Equal(Staff, h.ChangedById);
    }

    [Fact] public void Assigned_WithoutAssignee_Rejected()
    {
        var t = NewTicket();
        Assert.Throws<InvalidOperationException>(() => svc.ChangeStatus(t.Id, TicketStatus.Assigned, Admin, null));
    }

    [Fact] public void FullPath_Succeeds_AndRecordsEveryStep()
    {
        var t = NewTicket();
        svc.Assign(t.Id, Tech, Admin);
        svc.ChangeStatus(t.Id, TicketStatus.InProgress, Tech, "เริ่มตรวจ");
        svc.ChangeStatus(t.Id, TicketStatus.Resolved, Tech, "เปลี่ยน PSU");
        svc.ChangeStatus(t.Id, TicketStatus.Closed, Staff, "ใช้งานได้แล้ว");

        var h = Hist(t.Id);
        Assert.Equal(5, h.Count);
        Assert.Equal(new[] { Staff, Admin, Tech, Tech, Staff }, h.Select(x => x.ChangedById));
        Assert.Equal(TicketStatus.Closed, db.Tickets.Find(t.Id)!.Status);
    }

    [Fact] public void SkipStep_Rejected_NoHistoryAdded()
    {
        var t = NewTicket();
        Assert.Throws<InvalidOperationException>(() => svc.ChangeStatus(t.Id, TicketStatus.Resolved, Tech, null));
        Assert.Single(Hist(t.Id));
        Assert.Equal(TicketStatus.Open, db.Tickets.Find(t.Id)!.Status);
    }

    [Theory]
    [InlineData(TicketStatus.Open)] [InlineData(TicketStatus.InProgress)] [InlineData(TicketStatus.Cancelled)]
    public void Closed_CannotMove(TicketStatus to)
    {
        var t = NewTicket();
        svc.Assign(t.Id, Tech, Admin);
        svc.ChangeStatus(t.Id, TicketStatus.InProgress, Tech, null);
        svc.ChangeStatus(t.Id, TicketStatus.Resolved, Tech, null);
        svc.ChangeStatus(t.Id, TicketStatus.Closed, Staff, null);
        Assert.Throws<InvalidOperationException>(() => svc.ChangeStatus(t.Id, to, Admin, null));
    }

    [Fact] public void History_HasUserAndTime()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var t = NewTicket();
        svc.Assign(t.Id, Tech, Admin);
        Assert.All(Hist(t.Id), h => { Assert.True(h.ChangedById > 0); Assert.True(h.ChangedAt >= before); });
    }

    [Fact] public void Resolved_CanReopen()
    {
        var t = NewTicket();
        svc.Assign(t.Id, Tech, Admin);
        svc.ChangeStatus(t.Id, TicketStatus.InProgress, Tech, null);
        svc.ChangeStatus(t.Id, TicketStatus.Resolved, Tech, null);
        svc.ChangeStatus(t.Id, TicketStatus.InProgress, Staff, "ยังเสียอยู่");
        Assert.Equal(TicketStatus.InProgress, db.Tickets.Find(t.Id)!.Status);
    }

    [Fact] public void OnlyAdmin_CanAssign()
    {
        var t = NewTicket();
        Assert.Throws<InvalidOperationException>(() => svc.Assign(t.Id, Tech, Tech));
        Assert.Equal(TicketStatus.Assigned, svc.Assign(t.Id, Tech, Admin).Status);
    }

    [Fact] public void Staff_CannotResolve_OnlyAssignee()
    {
        var t = NewTicket();
        svc.Assign(t.Id, Tech, Admin);
        Assert.Throws<InvalidOperationException>(() => svc.ChangeStatus(t.Id, TicketStatus.InProgress, Staff, null));
        svc.ChangeStatus(t.Id, TicketStatus.InProgress, Tech, null);
        Assert.Throws<InvalidOperationException>(() => svc.ChangeStatus(t.Id, TicketStatus.Resolved, Staff, null));
        Assert.Equal(3, Hist(t.Id).Count);   // ที่ถูกปฏิเสธไม่บันทึกประวัติ
    }

    [Fact] public void Technician_CannotClose_OnlyReporter()
    {
        var t = NewTicket();
        svc.Assign(t.Id, Tech, Admin);
        svc.ChangeStatus(t.Id, TicketStatus.InProgress, Tech, null);
        svc.ChangeStatus(t.Id, TicketStatus.Resolved, Tech, null);
        Assert.Throws<InvalidOperationException>(() => svc.ChangeStatus(t.Id, TicketStatus.Closed, Tech, null));
        svc.ChangeStatus(t.Id, TicketStatus.Closed, Staff, null);
    }

    [Fact] public void Visibility_ByRole()
    {
        const int OtherStaff = 4;   // somying
        var mine = NewTicket();
        var other = svc.Create(new("เมาส์เสีย", "", 1, "Low", OtherStaff));
        svc.Assign(mine.Id, Tech, Admin);

        Assert.Equal(new[] { mine.Id }, svc.VisibleTo(Staff).Select(t => t.Id));        // เห็นแค่ของตัวเอง
        Assert.Equal(new[] { other.Id }, svc.VisibleTo(OtherStaff).Select(t => t.Id));
        Assert.Equal(new[] { mine.Id }, svc.VisibleTo(Tech).Select(t => t.Id));         // เห็นแค่งานที่ได้รับมอบหมาย
        Assert.Equal(2, svc.VisibleTo(Admin).Count());                                  // Admin เห็นทั้งหมด
    }

    [Fact] public void Passwords_AreHashed_AndVerify()
    {
        var admin = db.Users.Single(u => u.Username == "admin");
        Assert.DoesNotContain("admin1234", admin.PasswordHash);
        Assert.True(Passwords.Verify("admin1234", admin.PasswordHash));
        Assert.False(Passwords.Verify("wrong", admin.PasswordHash));
    }

    [Fact] public void Assign_ToStaff_Rejected()
    {
        var t = NewTicket();
        Assert.Throws<InvalidOperationException>(() => svc.Assign(t.Id, Staff, Admin));
    }
}
