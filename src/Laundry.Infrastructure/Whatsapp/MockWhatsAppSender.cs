using Laundry.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Laundry.Infrastructure.Whatsapp;

/// <summary>
/// Adapter development: pesan hanya dicatat di log, tidak mengirim apa pun ke nomor sungguhan.
/// Adapter produksi aktif setelah credential WhatsApp Business resmi tersedia (D-04, PRD 6.12).
/// </summary>
public sealed class MockWhatsAppSender(ILogger<MockWhatsAppSender> logger) : IWhatsAppSender
{
    public string Name => "mock-whatsapp";
    public bool IsLive => false;

    public Task<string> SendAsync(string templateKey, string targetPhone, string payloadJson,
        CancellationToken ct = default)
    {
        logger.LogInformation("[MOCK WA] {Template} → {Phone}: {Payload} (dev only, tidak mengirim sungguhan)",
            templateKey, targetPhone, payloadJson);
        return Task.FromResult($"mock-wa-{Guid.NewGuid():N}");
    }
}
