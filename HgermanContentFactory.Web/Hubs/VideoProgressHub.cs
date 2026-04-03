using Microsoft.AspNetCore.SignalR;

namespace HgermanContentFactory.Web.Hubs;

public class VideoProgressHub : Hub
{
    public async Task JoinChannel(string channelGroup) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, channelGroup);

    public async Task LeaveChannel(string channelGroup) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, channelGroup);
}

public static class VideoProgressHubExtensions
{
    public static async Task NotifyVideoStatusAsync(this IHubContext<VideoProgressHub> hub,
        int channelId, int videoId, string status, string? youtubeUrl = null)
    {
        await hub.Clients.Group($"channel-{channelId}").SendAsync("VideoStatusUpdated", new
        {
            videoId,
            status,
            youtubeUrl,
            timestamp = DateTime.UtcNow
        });
    }
}
