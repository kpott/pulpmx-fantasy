using Microsoft.AspNetCore.SignalR;

namespace PulpMXFantasy.Web.Hubs;

/// <summary>
/// SignalR hub for real-time admin notifications.
/// </summary>
/// <remarks>
/// Clients connect and join the "Admins" group to receive:
/// - Command started notifications
/// - Progress updates
/// - Completion/failure notifications
/// </remarks>
public class AdminHub : Hub
{
    /// <summary>
    /// Joins the Admins group to receive command status updates.
    /// </summary>
    public async Task JoinAdminGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
    }

    /// <summary>
    /// Leaves the Admins group.
    /// </summary>
    public async Task LeaveAdminGroup()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Admins");
    }
}
