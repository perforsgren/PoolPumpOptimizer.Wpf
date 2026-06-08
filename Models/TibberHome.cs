namespace PoolPumpOptimizer.Wpf.Models;

public sealed record TibberHome(
    string? Id,
    string? AppNickname,
    string? Address,
    bool HasCurrentSubscription)
{
    public string NicknameText =>
        string.IsNullOrWhiteSpace(AppNickname)
            ? "(no nickname)"
            : AppNickname;

    public string AddressText =>
        string.IsNullOrWhiteSpace(Address)
            ? "(no address)"
            : Address;

    public string SubscriptionText =>
        HasCurrentSubscription
            ? "subscription"
            : "no subscription";

    public string DisplayName =>
        $"{NicknameText} — {AddressText} — {SubscriptionText}";

    public override string ToString()
    {
        return NicknameText;
    }
}