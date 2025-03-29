using Microsoft.EntityFrameworkCore;

namespace BesTransactions.Models
{
    public class LogDbContext : DbContext
    {
        private readonly string _dpSuffix;

        public LogDbContext(string dbPrefix)
        {
            _dpSuffix = dbPrefix;
            Database.EnsureCreated();
        }

        public DbSet<Transaction> Transactions => Set<Transaction>();

        public DbSet<TransactionEvent> TransactionEvents => Set<TransactionEvent>();

        // Configure your database location and any other options here.
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.EnableSensitiveDataLogging(true);
            optionsBuilder.UseSqlite($"Data Source=BesTransactions-{_dpSuffix}.db");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure primary keys, etc.
            // Transaction TxId is the primary key
            modelBuilder.Entity<Transaction>().HasKey(t => t.TxId);

            // EF by default will create autoincrement PK on 'Id' for TransactionEvent / NextStep.
        }

        public void GateFailureReasonsQuery(DateTime start, DateTime end)
        {
            var result = Transactions
               .Where(
                   t =>
             // Filter for IsAbxCompleted being null or 0
             (!t.IsAbxCompleted.HasValue || t.IsAbxCompleted == true) &&
                       // Filter for FailureReason being null or within the given set
                       (t.FailureReason == null ||
                           new string[]
                           {
                               "DocumentReadFailure",
                               "ReaderIneligibleDocument",
                               "PersonDetectedOutsideExitDoor",
                               "ClearanceActionTimeout",
                               "Tailgating",
                               "VisionCameraBlocked",
                               "FaceCaptureFailure",
                               "DocumentNotSupported",
                               "EmergencyActivated",
                               "GateIsNotClear",
                               "Unknown",
                               "EGateDeactivated",
                               "UnhandledDeviceError",
                               "CommunicationError"
                           }.Contains(t.FailureReason)) &&
                       // Filter for LogDate range
                       t.LogDate >= DateTime.Parse("2025-03-11 23:00:00.000") &&
                       t.LogDate <= DateTime.Parse("2025-03-18 08:00:00.000"))
                            .GroupBy(
                                t =>
             // Group by computed ArabicFailureReason
             (t.FailureReason == null || new string[] { "Unknown", "GateIsNotClear", "CommunicationError" }.Contains(t.FailureReason))
                        ? "غير معروف"
                        : t.FailureReason == "DocumentReadFailure"
                        ? "فشل في قراءة المستند"
                        : t.FailureReason == "ReaderIneligibleDocument"
                        ? "بيانات الوثيقة غير صالحة"
                        : t.FailureReason == "PersonDetectedOutsideExitDoor"
                        ? "إستشعار وجود شخص أمام بوابة الخروج"
                        : t.FailureReason == "ClearanceActionTimeout"
                        ? "انتهاء الوقت المسموح لإجراءات تخليص السفر"
                        : t.FailureReason == "Tailgating"
                        ? "إستشعار وجود أكثر من شخص داخل البوابة"
                        : t.FailureReason == "VisionCameraBlocked"
                        ? "الكاميرا محجوبة (كاميرا استشعار المسافرين)"
                        : t.FailureReason == "FaceCaptureFailure"
                        ? "فشل في التقاط الوجه بعد ثلاث محاولات"
                        : t.FailureReason == "DocumentNotSupported"
                        ? "الوثيقة غير مدعومة (هوية خليجية غير السعودية والكويت)"
                        : t.FailureReason == "EmergencyActivated"
                        ? "تفعيل الطوارئ"
                        : t.FailureReason == "UnhandledDeviceError"
                        ? "خطأ بجهاز في البوابة"
                        : t.FailureReason == "EGateDeactivated"
                        ? "الغاء تنشيط البوابة"
                        : t.FailureReason)
                            .Select(g => new { ArabicFailureReason = g.Key, FailCount = g.Count() })
                            .OrderByDescending(x => x.FailCount);
        }
    }
}
