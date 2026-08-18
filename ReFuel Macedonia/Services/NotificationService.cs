using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Models;

namespace ReFuel_Macedonia.Services;

public interface INotificationService
{
    Task PushAsync(string userId, string titleKey, string bodyKey, NotificationKind kind, string? url = null, string? argument = null);
    Task<List<Notification>> GetAsync(string userId, int take = 20);
    Task<int> UnreadCountAsync(string userId);
    Task MarkReadAsync(string userId, int? id = null);
    Task<int> BroadcastPriceDropAsync(int fuelTypeId, decimal oldPrice, decimal newPrice);
}

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _db;

    public NotificationService(ApplicationDbContext db) => _db = db;

    public async Task PushAsync(string userId, string titleKey, string bodyKey, NotificationKind kind, string? url = null, string? argument = null)
    {
        if (string.IsNullOrEmpty(userId)) return;

        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = titleKey,
            Body = string.IsNullOrEmpty(argument) ? bodyKey : $"{bodyKey}|{argument}",
            Kind = kind,
            Url = url
        });

        await _db.SaveChangesAsync();
    }

    public Task<List<Notification>> GetAsync(string userId, int take = 20)
        => _db.Notifications
              .Where(n => n.UserId == userId)
              .OrderByDescending(n => n.CreatedAtUtc)
              .Take(take)
              .ToListAsync();

    public Task<int> UnreadCountAsync(string userId)
        => _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

    public async Task MarkReadAsync(string userId, int? id = null)
    {
        var query = _db.Notifications.Where(n => n.UserId == userId && !n.IsRead);
        if (id.HasValue) query = query.Where(n => n.Id == id.Value);

        foreach (var n in await query.ToListAsync()) n.IsRead = true;
        await _db.SaveChangesAsync();
    }

    public async Task<int> BroadcastPriceDropAsync(int fuelTypeId, decimal oldPrice, decimal newPrice)
    {
        if (newPrice >= oldPrice) return 0;

        var userIds = await _db.Users
            .Where(u => u.NotifyPriceDrops)
            .Select(u => u.Id)
            .ToListAsync();

        var delta = Math.Round(oldPrice - newPrice, 2);

        foreach (var id in userIds)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = id,
                Title = "notif.pricedrop.title",
                Body = $"notif.pricedrop.body|{delta}",
                Kind = NotificationKind.PriceDrop,
                Url = "/Prices"
            });
        }

        await _db.SaveChangesAsync();
        return userIds.Count;
    }
}
