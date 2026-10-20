using ScreenHire.Infrastructure.Tenancy;
using Wolverine;

namespace ScreenHire.Application.Messaging;

/// <summary>
/// Wolverine middleware on every handler of an <see cref="ITenantMessage"/>: makes the message's organization the
/// tenant of the handler's scope before the handler runs. <see cref="TenantSessionInterceptor"/> then sets
/// <c>app.tenant_id</c> inside the handler's transaction, so row-level security applies to the worker as it does to a
/// request. Registered in <see cref="ScreenHireMessaging.ConfigureHandler"/>.
/// </summary>
public static class TenantMessageMiddleware
{
    // The envelope, not the message: Wolverine binds a middleware parameter by the handler's concrete message type, so a
    // parameter typed as the interface cannot be resolved.
    public static void Before(Envelope envelope, TenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(tenant);
        if (envelope.Message is ITenantMessage message)
        {
            tenant.SetOrganization(message.OrganizationId);
        }
    }
}
