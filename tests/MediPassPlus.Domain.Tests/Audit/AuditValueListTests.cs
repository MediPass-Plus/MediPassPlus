using MediPassPlus.Domain.Audit;

namespace MediPassPlus.Domain.Tests.Audit;

public class AuditValueListTests
{
    [Fact]
    public void AuditAction_matches_adr_0004()
    {
        var expected = new[]
        {
            "RecordRead",
            "RecordWritten",
            "DiagnosisCreated",
            "PrescriptionCreated",
            "ReportUploaded",
            "ReportDownloaded",
            "AccessGrantCreated",
            "AccessRequestRejected",
            "AccessEnded"
        };

        Assert.Equal(expected.OrderBy(name => name), Enum.GetNames<AuditAction>().OrderBy(name => name));
    }

    [Fact]
    public void AuditEntityType_matches_adr_0004()
    {
        var expected = new[]
        {
            "MedicalRecord",
            "Diagnosis",
            "Prescription",
            "Report",
            "AccessRequest",
            "AccessGrant"
        };

        Assert.Equal(expected.OrderBy(name => name), Enum.GetNames<AuditEntityType>().OrderBy(name => name));
    }
}
