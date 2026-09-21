namespace RatBot.Features.EmojiAnalytics;

public sealed record EmojiUsagePage(IReadOnlyList<EmojiUsageCount> Items, int Page, int TotalPages, int TotalCount);
