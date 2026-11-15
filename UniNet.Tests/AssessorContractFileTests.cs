using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UniNet.API.Services;
using UniNet.Application.Services;
using UniNet.Domain;
using Xunit;

namespace UniNet.Tests;

public sealed class AssessorContractFileTests
{
    [Fact]
    public async Task VietnamesePdfRoundTripsAndStoredHashDetectsTampering()
    {
        var root = Path.Combine(Path.GetTempPath(), "uninet-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var font = Environment.GetEnvironmentVariable("ASSESSOR_TEST_FONT") ??
            (OperatingSystem.IsWindows() ? @"C:\Windows\Fonts\arial.ttf" : "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");
        Assert.True(File.Exists(font), "Set ASSESSOR_TEST_FONT to a TrueType font supporting Vietnamese.");
        try
        {
            var template = Path.Combine(root, "template.txt");
            await File.WriteAllTextAsync(template,
                "HỢP ĐỒNG THỬ NGHIỆM\n{{FullName}}\n{{Email}}\n{{CitizenId}}\n{{BankAccountHolder}}\n{{SignatureStatement}}\n" +
                new string('W', 255) + "\n" +
                string.Join("\n", Enumerable.Repeat("Đây là nội dung kiểm thử để xác nhận phân trang và hiển thị tiếng Việt.", 55)), Encoding.UTF8);
            var files = new AssessorContractFiles(Options.Create(new AssessorContractOptions
            {
                TemplatePath = template, FontPath = font, StoragePath = Path.Combine(root, "private"), TemplateVersion = "TEST-V1",
                SignatureStatement = "Tôi xác nhận đã đọc và đồng ý với bản thử nghiệm."
            }));
            var stored = await files.Generate(new AssessorOnboarding
            {
                FullName = "Nguyễn Thị Ánh", EmailNormalized = "test@example.test"
            }, new("000000000000", "0123456789", "Ngân hàng thử nghiệm", "000123456789", "NGUYEN THI ANH"), default);
            var bytes = await files.Read(stored.Key, stored.Sha256, default);
            using var pdf = PdfDocument.Open(bytes);
            Assert.True(pdf.NumberOfPages >= 2);
            var text = string.Join("\n", pdf.GetPages().Select(p => p.Text));
            Assert.Contains("Nguyễn Thị Ánh", text);
            Assert.Contains("000000000000", text);
            Assert.DoesNotContain("{{", text);
            foreach (var page in pdf.GetPages())
                Assert.All(page.Letters, letter => {
                    Assert.InRange(letter.BoundingBox.Left, 50, 545);
                    Assert.InRange(letter.BoundingBox.Right, 50, 545);
                    Assert.InRange(letter.BoundingBox.Bottom, 40, 805);
                });
            var preview = Environment.GetEnvironmentVariable("ASSESSOR_TEST_PDF_PREVIEW");
            if (!string.IsNullOrWhiteSpace(preview)) await File.WriteAllBytesAsync(preview, bytes);
            await File.WriteAllBytesAsync(Path.Combine(root, "private", stored.Key), new byte[] { 0, 1, 2 });
            await Assert.ThrowsAsync<InvalidOperationException>(() => files.Read(stored.Key, stored.Sha256, default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => files.Read("../outside.pdf", stored.Sha256, default));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DevelopmentPickupWritesEmailWithoutCallingAnExternalProvider()
    {
        var root = Path.Combine(Path.GetTempPath(), "uninet-mail-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sender = new AssessorSmtpSender(Options.Create(new AssessorSmtpOptions {
                From = "noreply@example.test", PickupDirectory = root
            }), new DevelopmentEnvironment());
            await sender.Send(new("assessor@example.test", "OTP kiểm thử", "Mã OTP thử nghiệm 123456"), "test-key", default);
            Assert.Single(Directory.GetFiles(root, "*.eml"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class DevelopmentEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "UniNet.Tests";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
