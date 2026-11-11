using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Ironite.Launcher.Services.Friends;

/// <summary>
/// The player's Ironite account and friends: keeps them signed in (the refresh token is saved encrypted for
/// this Windows user in account.json), checks in every minute so friends see them online and what they're
/// playing, and refreshes the friends list every 30 seconds while signed in.
/// </summary>
public sealed class FriendsService
{
    // The Supabase project the launcher uses. The anon key is meant to ship in apps; the database only allows
    // what supabase/friends.sql's functions allow.
    public const string SupabaseUrl = "https://uejyssvtwcrsuuntdjlf.supabase.co";
    public const string SupabaseAnonKey = "sb_publishable_eIxyYaCLELj8JK9A7ivNmA_MxVGKhbB";

    private readonly IFriendsBackend? _backend;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Session? _session;
    private string? _lastGame;
    private CancellationTokenSource? _loop;

    public FriendsService()
    {
        var url = Environment.GetEnvironmentVariable("IRONITE_FRIENDS_URL") is { Length: > 0 } u ? u : SupabaseUrl;
        var key = Environment.GetEnvironmentVariable("IRONITE_FRIENDS_KEY") is { Length: > 0 } k ? k : SupabaseAnonKey;
        if (url == "fake") _backend = new FakeBackend();
        else if (url.Length > 0 && key.Length > 0) _backend = new SupabaseBackend(url, key);
    }

    /// <summary>False until the launcher has a Friends server to talk to (the page says "coming soon").</summary>
    public bool Available => _backend != null;
    public bool SignedIn => _session != null;
    public string Username { get; private set; } = "";
    public string Email => _session?.Email ?? "";
    public List<Friend> Friends { get; private set; } = new();
    public List<FriendRequest> Requests { get; private set; } = new();
    public string? Error { get; private set; }
    public bool Loaded { get; private set; }
    /// <summary>Bumps whenever something shown on the Friends page changes.</summary>
    public int Version { get; private set; }

    /// <summary>The player's own profile (theme, favourite game, sharing), once signed in.</summary>
    public Profile? MyProfile { get; private set; }
    public string MyId => _session?.UserId ?? "";

    /// <summary>Asks the launcher for the player's games (play time, achievements) to share with friends.</summary>
    public Func<Task<List<SharedGame>>>? GamesSnapshot { get; set; }

    // Profile pictures by player id, with when they were last changed.
    private readonly Dictionary<string, (DateTime? At, System.Windows.Media.ImageSource? Image)> _avatars = new();
    private DateTime _lastSync;

    /// <summary>A player's profile picture, if they have one and it has been downloaded.</summary>
    public System.Windows.Media.ImageSource? Avatar(string userId) =>
        _avatars.TryGetValue(userId, out var a) ? a.Image : null;

    /// <summary>Asks the launcher what the player is playing right now (game id and title), for their friends to see.</summary>
    public Func<(string? Id, string? Title)>? NowPlaying { get; set; }

    private static string AccountFile => Path.Combine(AppPaths.DataDir, "account.json");
    private IFriendsBackend Backend => _backend ?? throw new FriendsException("Friends isn't available yet.");

    // ---------------------------------------------------------------- start-up

    /// <summary>Signs back in with the saved account, if there is one.</summary>
    public async Task StartAsync()
    {
        if (_backend == null) return;
        var saved = LoadSaved();
        if (saved == null) { Loaded = true; Version++; return; }
        try
        {
            await SetSession(await _backend.RefreshAsync(saved.Value.Refresh), saved.Value.Username);
        }
        catch (FriendsException ex) when (ex.SignedOut)
        {
            Log.Info("friends: saved sign-in expired");
            Forget();
        }
        catch (FriendsException ex)
        {
            // Offline: keep the saved account and try again later.
            Error = ex.Message;
            Username = saved.Value.Username;
            _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromMinutes(1)); if (_session == null) await StartAsync(); });
        }
        Loaded = true;
        Version++;
    }

    private async Task SetSession(Session s, string? username = null)
    {
        _session = s;
        Username = username ?? "";
        try { Username = await Backend.MyUsernameAsync(s); } catch (Exception ex) { Log.Info($"friends: me: {ex.Message}"); }
        Save();
        Error = null;
        Log.Info($"friends: signed in as {Username}");
        await LoadMyProfileAsync();
        StartLoop();
    }

    private void StartLoop()
    {
        _loop?.Cancel();
        _loop = new CancellationTokenSource();
        var ct = _loop.Token;
        _ = Task.Run(async () =>
        {
            var n = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var (id, title) = NowPlaying?.Invoke() ?? (null, null);
                    var stopped = _lastGame != null && id == null;
                    if (n % 2 == 0 || id != _lastGame) await Call(s => Backend.HeartbeatAsync(s, id, title)); // every minute, or right away when a game starts/stops
                    _lastGame = id;
                    await RefreshAsync();
                    // Shared games: when signing in, after playing, and every 15 minutes.
                    if (n == 0 || stopped || DateTime.UtcNow - _lastSync > TimeSpan.FromMinutes(15)) await SyncGamesAsync();
                }
                catch (Exception ex) { Log.Info($"friends: {ex.Message}"); }
                n++;
                try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { return; }
            }
        });
    }

    /// <summary>Re-reads friends and requests now.</summary>
    public async Task RefreshAsync()
    {
        if (_session == null) return;
        try
        {
            var friends = await Call(s => Backend.FriendsAsync(s));
            var requests = await Call(s => Backend.RequestsAsync(s));
            Friends = friends;
            Requests = requests;
            Error = null;
            await UpdateAvatarsAsync(friends.Select(f => (f.Id, f.AvatarAt)));
        }
        catch (FriendsException ex) { Error = ex.Message; }
        Version++;
    }

    /// <summary>Calls the server with a fresh session (refreshing it when it's about to expire).</summary>
    private async Task<T> Call<T>(Func<Session, Task<T>> f)
    {
        var s = await FreshSession();
        try { return await f(s); }
        catch (FriendsException ex) when (ex.SignedOut)
        {
            SignedOutHere();
            throw;
        }
    }

    private Task Call(Func<Session, Task> f) => Call<bool>(async s => { await f(s); return true; });

    private async Task<Session> FreshSession()
    {
        await _gate.WaitAsync();
        try
        {
            var s = _session ?? throw new FriendsException("Sign in first.");
            if (DateTime.UtcNow < s.ExpiresAt) return s;
            try
            {
                _session = await Backend.RefreshAsync(s.RefreshToken);
                Save();
                return _session;
            }
            catch (FriendsException ex) when (ex.SignedOut)
            {
                SignedOutHere();
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private void SignedOutHere()
    {
        _loop?.Cancel();
        _session = null;
        Forget();
        Friends = new();
        Requests = new();
        Version++;
    }

    // ---------------------------------------------------------------- profiles and shared games

    private async Task LoadMyProfileAsync()
    {
        try
        {
            MyProfile = await Call(s => Backend.ProfileAsync(s, s.UserId));
            if (MyProfile != null) await UpdateAvatarsAsync(new[] { (MyProfile.Id, MyProfile.AvatarAt) });
        }
        catch (Exception ex) { Log.Info($"friends: my profile: {ex.Message}"); }
        Version++;
    }

    /// <summary>Downloads the profile pictures that are new or changed since they were last downloaded.</summary>
    private async Task UpdateAvatarsAsync(IEnumerable<(string Id, DateTime? At)> who)
    {
        var need = new List<string>();
        foreach (var (id, at) in who)
        {
            if (_avatars.TryGetValue(id, out var have) && have.At == at) continue;
            if (at == null) { _avatars[id] = (null, null); continue; }
            need.Add(id);
        }
        if (need.Count == 0) return;
        try
        {
            var pics = await Call(s => Backend.AvatarsAsync(s, need));
            foreach (var (id, at) in who)
                if (need.Contains(id))
                {
                    System.Windows.Media.ImageSource? img = null;
                    if (pics.TryGetValue(id, out var bytes))
                        using (var ms = new MemoryStream(bytes)) img = Views.Ui.Decode(ms, 160);
                    _avatars[id] = (at, img);
                }
            Version++;
        }
        catch (Exception ex) { Log.Info($"friends: pictures: {ex.Message}"); }
    }

    public async Task SyncGamesAsync()
    {
        _lastSync = DateTime.UtcNow;
        if (_session == null || GamesSnapshot == null || MyProfile?.ShareGames == false) return;
        try
        {
            var games = await GamesSnapshot();
            await Call(s => Backend.SyncGamesAsync(s, games));
            Log.Info($"friends: shared {games.Count} games");
        }
        catch (Exception ex) { Log.Info($"friends: share games: {ex.Message}"); }
    }

    /// <summary>A friend's profile and games (or your own, with your id).</summary>
    public async Task<(Profile? Profile, FriendLibrary Library)> LoadProfileAsync(string userId)
    {
        var profile = await Call(s => Backend.ProfileAsync(s, userId));
        if (profile != null) await UpdateAvatarsAsync(new[] { (profile.Id, profile.AvatarAt) });
        FriendLibrary lib;
        if (userId == MyId)
        {
            // Your own: straight from this launcher (what friends see once it's shared).
            var mine = GamesSnapshot != null ? await GamesSnapshot() : new();
            lib = new FriendLibrary(profile?.ShareGames != false, mine.OrderByDescending(g => g.LastPlayed).ThenByDescending(g => g.PlaySeconds).ToList());
        }
        else lib = await Call(s => Backend.FriendGamesAsync(s, userId));
        return (profile, lib);
    }

    /// <summary>Saves the player's profile. avatarJpeg: null = keep the picture, empty = remove it.</summary>
    public async Task SaveProfileAsync(string theme, string? favoriteGame, string? favoriteTitle, byte[]? avatarJpeg)
    {
        await Call(s => Backend.SaveProfileAsync(s, theme, favoriteGame, favoriteTitle, avatarJpeg != null, avatarJpeg is { Length: > 0 } ? avatarJpeg : null));
        if (avatarJpeg != null) _avatars.Remove(MyId);
        await LoadMyProfileAsync();
    }

    public async Task SetShareGamesAsync(bool share)
    {
        await Call(s => Backend.SetShareGamesAsync(s, share));
        await LoadMyProfileAsync();
        if (share) await SyncGamesAsync();
    }

    // ---------------------------------------------------------------- accounts

    public static string? CheckUsername(string u) =>
        u.Length is < 3 or > 20 || !u.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? "Usernames are 3 to 20 letters, numbers or underscores." : null;

    public Task<bool> UsernameAvailableAsync(string u) => Backend.UsernameAvailableAsync(u);

    /// <summary>Registers. True when signed in straight away; false when the email must be confirmed with a code first.</summary>
    public async Task<bool> SignUpAsync(string email, string password, string username)
    {
        if (CheckUsername(username) is { } bad) throw new FriendsException(bad);
        if (password.Length < 8) throw new FriendsException("Choose a password of at least 8 characters.");
        if (!await Backend.UsernameAvailableAsync(username)) throw new FriendsException($"The username {username} is taken. Choose another.");
        var s = await Backend.SignUpAsync(email.Trim(), password, username);
        if (s != null)
        {
            Log.Info("friends: registered");
            await SetSession(s, username);
            return true;
        }
        Log.Info("friends: registered, waiting for the email code");
        return false;
    }

    /// <summary>For the self-test: can the launcher talk to the Friends server (and is its database set up)?</summary>
    public async Task<string> CheckServerAsync()
    {
        if (_backend == null) return "not configured";
        try { await _backend.UsernameAvailableAsync("ironite_check"); return "ok"; }
        catch (Exception ex) { return ex.Message; }
    }

    public async Task ConfirmEmailAsync(string email, string code) => await SetSession(await Backend.ConfirmEmailAsync(email.Trim(), code));
    public Task ResendConfirmationAsync(string email) => Backend.ResendConfirmationAsync(email.Trim());
    public async Task SignInAsync(string email, string password) => await SetSession(await Backend.SignInAsync(email.Trim(), password));
    public Task SendPasswordResetAsync(string email) => Backend.SendPasswordResetAsync(email.Trim());

    public async Task ResetPasswordAsync(string email, string code, string password)
    {
        if (password.Length < 8) throw new FriendsException("Choose a password of at least 8 characters.");
        await SetSession(await Backend.ResetPasswordAsync(email.Trim(), code, password));
    }

    public async Task SignOutAsync()
    {
        if (_session is { } s)
        {
            try { await Backend.GoOfflineAsync(s); } catch { /* offline */ }
            await Backend.SignOutAsync(s);
        }
        SignedOutHere();
        Username = "";
        MyProfile = null;
        Log.Info("friends: signed out");
    }

    /// <summary>When the launcher closes: show as offline straight away instead of after two minutes.</summary>
    public void GoOffline()
    {
        if (_session is not { } s) return;
        try { Backend.GoOfflineAsync(s).Wait(TimeSpan.FromSeconds(2)); } catch { /* offline */ }
    }

    // ---------------------------------------------------------------- friends

    public async Task<string> SendRequestAsync(string username)
    {
        username = username.Trim().TrimStart('@');
        if (username.Length == 0) throw new FriendsException("Type your friend's username.");
        var r = await Call(s => Backend.SendRequestAsync(s, username));
        await RefreshAsync();
        return r;
    }

    public async Task AnswerAsync(string fromId, bool accept) { await Call(s => Backend.AnswerRequestAsync(s, fromId, accept)); await RefreshAsync(); }
    public async Task CancelAsync(string toId) { await Call(s => Backend.CancelRequestAsync(s, toId)); await RefreshAsync(); }
    public async Task RemoveAsync(string friendId) { await Call(s => Backend.RemoveFriendAsync(s, friendId)); await RefreshAsync(); }

    // ---------------------------------------------------------------- saved sign-in (encrypted for this Windows user)

    private sealed class Saved
    {
        public string Email { get; set; } = "";
        public string Username { get; set; } = "";
        public string Token { get; set; } = "";
    }

    private void Save()
    {
        if (_session is not { } s) return;
        try
        {
            JsonStore.Save(AccountFile, new Saved { Email = s.Email, Username = Username, Token = Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(s.RefreshToken))) });
        }
        catch (Exception ex) { Log.Error("friends: save account", ex); }
    }

    private static (string Refresh, string Username)? LoadSaved()
    {
        try
        {
            if (!File.Exists(AccountFile)) return null;
            var saved = JsonStore.Load(AccountFile, () => new Saved());
            if (saved.Token.Length == 0) return null;
            return (Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(saved.Token))), saved.Username);
        }
        catch (Exception ex)
        {
            Log.Info($"friends: saved account unreadable ({ex.Message})");
            return null;
        }
    }

    private static void Forget() => GameInstaller.TryDelete(AccountFile);

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr h);

    /// <summary>Windows DPAPI: only this Windows user on this PC can read it back.</summary>
    private static byte[] Protect(byte[] data) => Dpapi(data, true);
    private static byte[] Unprotect(byte[] data) => Dpapi(data, false);

    private static byte[] Dpapi(byte[] data, bool protect)
    {
        var h = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new Blob { Size = data.Length, Data = h.AddrOfPinnedObject() };
            var output = new Blob();
            var ok = protect
                ? CryptProtectData(ref input, "Ironite Launcher", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref output);
            if (!ok) throw new InvalidOperationException("DPAPI error " + Marshal.GetLastWin32Error());
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            LocalFree(output.Data);
            return result;
        }
        finally { h.Free(); }
    }

    /// <summary>For the self-test: the encryption round-trips.</summary>
    public static bool DpapiWorks()
    {
        var b = Encoding.UTF8.GetBytes("ironite-test");
        return Encoding.UTF8.GetString(Unprotect(Protect(b))) == "ironite-test";
    }
}
