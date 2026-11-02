namespace SuperImoti.Web.ViewModels;

/// <summary>Model of the _ListingGrid partial.</summary>
public sealed record ListingGridViewModel(
    IReadOnlyList<ListingCardViewModel> Items,
    int Columns = 3,
    string? EmptyText = null);

/// <summary>Model of the _FavoriteButton partial.</summary>
public sealed record FavoriteButtonViewModel(int ListingId, bool IsFavorite, bool Compact = true);

public class ListingDeleteViewModel
{
    public required ListingCardViewModel Card { get; init; }

    public int CommentCount { get; init; }

    public int FavoriteCount { get; init; }

    public bool IsImported { get; init; }
}

public class ProfilePageViewModel
{
    public required ProfileViewModel Input { get; init; }

    public string Email { get; init; } = string.Empty;

    public DateTime CreatedUtc { get; init; }

    public int ListingCount { get; init; }

    public int FavoriteCount { get; init; }

    public int CommentCount { get; init; }

    public bool IsAdmin { get; init; }
}
