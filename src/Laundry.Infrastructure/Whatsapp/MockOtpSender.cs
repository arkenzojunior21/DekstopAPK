using Laundry.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Laundry.Infrastructure.Whatsapp;

/// <summary>
/// Adapter development: OTP hanya dicatat di log, tidak mengirim apa pun.
/// Adapter produksi aktif setelah credential WhatsApp Business resmi tersedia (D-04, PRD 6.12).
/// </summary>
public sealed class MockOtpSender(ILogger<MockOtpSender> logger) : IOtpSender
{
    public Task SendOtpAsync(string phone, string code, CancellationToken ct = default)
    {
        logger.LogInformation("[MOCK WA] OTP {Phone}: {Code} (dev only, tidak mengirim sungguhan)", phone, code);
        return Task.CompletedTask;
    }
}
