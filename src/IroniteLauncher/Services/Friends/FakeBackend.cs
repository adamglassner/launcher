namespace Ironite.Launcher.Services.Friends;

/// <summary>
/// An in-memory Friends server for CI's screenshot tour (IRONITE_FRIENDS_URL=fake): the code "123456" confirms
/// any email, and a few made-up players are friends already.
/// </summary>
public sealed class FakeBackend : IFriendsBackend
{
    private readonly Dictionary<string, (string Password, string Username, bool Confirmed)> _users = new();
    private readonly List<Friend> _friends = new()
    {
        new("f1", "NovaPilot", true, "solarsiege", "Solar Siege", DateTime.UtcNow, DateTime.UtcNow.AddDays(-20), "ocean"),
        new("f2", "MapleQueen", true, null, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(-12), "rose"),
        new("f3", "CardShark_42", false, null, null, DateTime.UtcNow.AddHours(-5), DateTime.UtcNow.AddDays(-3), "forest"),
    };
    private readonly List<FriendRequest> _requests = new()
    {
        new("r1", "LuckyDraw", true, DateTime.UtcNow.AddHours(-2)),
        new("r2", "Pathfinder7", false, DateTime.UtcNow.AddDays(-1)),
    };

    private static Session S(string email) => new("fake", "fake-refresh:" + email, DateTime.UtcNow.AddHours(1), "me", email);

    public Task<bool> UsernameAvailableAsync(string username) => Task.FromResult(_users.Values.All(u => !u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)));

    public Task<Session?> SignUpAsync(string email, string password, string username)
    {
        if (_users.ContainsKey(email)) throw new FriendsException("There's already an account with that email. Sign in instead.");
        _users[email] = (password, username, true); // like a server with email confirmation off
        return Task.FromResult<Session?>(S(email));
    }

    public Task<Session> ConfirmEmailAsync(string email, string code)
    {
        if (code != "123456" || !_users.TryGetValue(email, out var u)) throw new FriendsException("That code is wrong or has expired. Check the email, or send a new code.");
        _users[email] = u with { Confirmed = true };
        return Task.FromResult(S(email));
    }

    public Task ResendConfirmationAsync(string email) => Task.CompletedTask;

    public Task<Session> SignInAsync(string email, string password)
    {
        if (!_users.TryGetValue(email, out var u) || u.Password != password) throw new FriendsException("That email and password don't match an account.");
        if (!u.Confirmed) throw new FriendsException("Confirm your email first: enter the code we sent you.");
        return Task.FromResult(S(email));
    }

    public Task<Session> RefreshAsync(string refreshToken) => Task.FromResult(S(refreshToken.Replace("fake-refresh:", "")));
    public Task SignOutAsync(Session s) => Task.CompletedTask;
    public Task SendPasswordResetAsync(string email) => Task.CompletedTask;
    public Task<Session> ResetPasswordAsync(string email, string code, string newPassword) => ConfirmEmailAsync(email, code);

    public Task<string> MyUsernameAsync(Session s) => Task.FromResult(_users.TryGetValue(s.Email, out var u) ? u.Username : "Player");
    public Task HeartbeatAsync(Session s, string? game, string? gameTitle) => Task.CompletedTask;
    public Task GoOfflineAsync(Session s) => Task.CompletedTask;
    public Task<List<Friend>> FriendsAsync(Session s) => Task.FromResult(_friends.ToList());
    public Task<List<FriendRequest>> RequestsAsync(Session s) => Task.FromResult(_requests.ToList());

    public Task<string> SendRequestAsync(Session s, string username)
    {
        if (_friends.Any(f => f.Username.Equals(username, StringComparison.OrdinalIgnoreCase))) return Task.FromResult("already_friends");
        _requests.Add(new("r" + _requests.Count + 10, username, false, DateTime.UtcNow));
        return Task.FromResult("sent");
    }

    public Task AnswerRequestAsync(Session s, string fromId, bool accept)
    {
        var r = _requests.First(x => x.Id == fromId);
        _requests.Remove(r);
        if (accept) _friends.Add(new(r.Id, r.Username, false, null, null, null, DateTime.UtcNow));
        return Task.CompletedTask;
    }

    public Task CancelRequestAsync(Session s, string toId) { _requests.RemoveAll(r => r.Id == toId); return Task.CompletedTask; }
    public Task RemoveFriendAsync(Session s, string friendId) { _friends.RemoveAll(f => f.Id == friendId); return Task.CompletedTask; }

    private string _theme = "ember";
    private string? _fav, _favTitle;
    private bool _share = true;
    private List<SharedGame> _mine = new();

    public Task<Profile?> ProfileAsync(Session s, string userId)
    {
        if (userId == "me") return Task.FromResult<Profile?>(new("me", _users.TryGetValue(s.Email, out var u) ? u.Username : "Player", _theme, _fav, _favTitle, null, _share, DateTime.UtcNow));
        var f = _friends.FirstOrDefault(x => x.Id == userId);
        return Task.FromResult<Profile?>(f == null ? null : new(f.Id, f.Username, f.Theme, "solarsiege", "Solar Siege", null, true, DateTime.UtcNow.AddDays(-40)));
    }

    public Task SaveProfileAsync(Session s, string theme, string? favoriteGame, string? favoriteTitle, bool setAvatar, byte[]? avatarJpeg)
    {
        (_theme, _fav, _favTitle) = (theme, favoriteGame, favoriteTitle);
        return Task.CompletedTask;
    }

    public Task<Dictionary<string, byte[]>> AvatarsAsync(Session s, IEnumerable<string> userIds) => Task.FromResult(new Dictionary<string, byte[]>());
    public Task SyncGamesAsync(Session s, List<SharedGame> games) { _mine = games; return Task.CompletedTask; }
    public Task SetShareGamesAsync(Session s, bool share) { _share = share; return Task.CompletedTask; }

    public Task<FriendLibrary> FriendGamesAsync(Session s, string friendId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        List<SharedAchievement> Ach(params (string Name, bool On)[] xs) => xs.Select((x, i) => new SharedAchievement(x.Name, "An achievement", x.On, x.On ? now - i * 86400 : 0, null)).ToList();
        return Task.FromResult(new FriendLibrary(true, new()
        {
            new("solarsiege", "Solar Siege", "ironite", true, 14 * 3600 + 1200, 31, now - 600, 23, 54, Ach(("First Contact", true), ("Pluto Patrol", true), ("Boss Rush", false))),
            new("klondike", "Klondike", "ironite", true, 3 * 3600, 12, now - 86400 * 2, 8, 20, Ach(("Gold Rush", true), ("Clean Sweep", false))),
            new("hearts", "Hearts", "ironite", true, 5400, 6, now - 86400 * 5, 0, 0, null),
            new("custom-1", "Balatro", "own", true, 9 * 3600, 20, now - 86400 * 9, 140, 150, null),
        }));
    }
}
