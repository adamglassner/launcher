using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ironite.Launcher.Services.Friends;

/// <summary>
/// Friends on Supabase: accounts through Supabase Auth (email + password, confirmed with an emailed code) and
/// everything else through the database functions in supabase/friends.sql (/rest/v1/rpc/...).
/// </summary>
public sealed class SupabaseBackend(string url, string anonKey) : IFriendsBackend
{
    private readonly string _url = url.TrimEnd('/');
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body, Session? s = null)
    {
        using var req = new HttpRequestMessage(method, _url + path);
        req.Headers.TryAddWithoutValidation("apikey", anonKey);
        // Signed out, only the apikey header goes (Supabase's sb_publishable_ keys aren't tokens).
        if (s != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.AccessToken);
        req.Headers.UserAgent.ParseAdd("IroniteLauncher/" + App.VersionText);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        HttpResponseMessage resp;
        try { resp = await Http.SendAsync(req); }
        catch (Exception ex) { Log.Info($"friends: {path}: {ex.Message}"); throw new FriendsException("Couldn't reach the Friends server. Check your internet connection."); }
        using (resp)
        {
            var text = await resp.Content.ReadAsStringAsync();
            JsonNode? json = null;
            try { if (text.Length > 0) json = JsonNode.Parse(text); } catch { /* not JSON */ }
            if (resp.IsSuccessStatusCode) return json;
            var msg = Str(json, "msg") ?? Str(json, "message") ?? Str(json, "error_description") ?? Str(json, "error") ?? resp.ReasonPhrase ?? "";
            var code = Str(json, "error_code") ?? Str(json, "code") ?? "";
            Log.Info($"friends: {path}: {(int)resp.StatusCode} {code} {msg}");
            if (resp.StatusCode == HttpStatusCode.Unauthorized || code is "bad_jwt" or "session_not_found" or "PGRST301" or "PGRST303")
                throw new FriendsException("Please sign in again.", signedOut: s != null && path.StartsWith("/rest"));
            throw new FriendsException(Friendly(code, msg, (int)resp.StatusCode));
        }
    }

    private static string? Str(JsonNode? n, string key) =>
        n is JsonObject o && o.TryGetPropertyValue(key, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    /// <summary>Supabase's error codes in plain words.</summary>
    private static string Friendly(string code, string msg, int status) => code switch
    {
        "invalid_credentials" => "That email and password don't match an account.",
        "email_not_confirmed" => "Confirm your email first: enter the code we sent you.",
        "user_already_exists" or "email_exists" => "There's already an account with that email. Sign in instead.",
        "weak_password" => "Choose a stronger password (at least 8 characters).",
        "otp_expired" => "That code is wrong or has expired. Check the email, or send a new code.",
        "over_email_send_rate_limit" or "over_request_rate_limit" => "Too many tries. Wait a minute and try again.",
        "email_address_invalid" or "validation_failed" => "Check the email address.",
        "signup_disabled" => "New accounts can't be made right now.",
        "unexpected_failure" when msg.Contains("saving new user", StringComparison.OrdinalIgnoreCase) => "That username was just taken. Choose another.",
        _ when msg.Contains("Database error saving new user", StringComparison.OrdinalIgnoreCase) => "That username was just taken. Choose another.",
        _ when status >= 500 => "The Friends server had a problem. Try again in a minute.",
        _ => msg.Length > 0 ? msg : "Something went wrong.",
    };

    private static Session ToSession(JsonNode? j)
    {
        var token = Str(j, "access_token") ?? throw new FriendsException("The server didn't sign you in.");
        var expires = j!["expires_in"]?.GetValue<int>() ?? 3600;
        return new Session(token, Str(j, "refresh_token") ?? "", DateTime.UtcNow.AddSeconds(expires - 60),
            Str(j!["user"], "id") ?? "", Str(j["user"], "email") ?? "");
    }

    private Task<JsonNode?> Rpc(string fn, object? args, Session? s) => SendAsync(HttpMethod.Post, "/rest/v1/rpc/" + fn, args ?? new { }, s);

    // ---- accounts

    public async Task<bool> UsernameAvailableAsync(string username) =>
        (await Rpc("username_available", new { name = username }, null))?.GetValue<bool>() == true;

    public async Task<Session?> SignUpAsync(string email, string password, string username)
    {
        var j = await SendAsync(HttpMethod.Post, "/auth/v1/signup", new { email, password, data = new { username } });
        // With email confirmation on, an email that already has an account gets a made-up user with no identities.
        if (j?["identities"] is JsonArray { Count: 0 } || j?["user"]?["identities"] is JsonArray { Count: 0 })
            throw new FriendsException("There's already an account with that email. Sign in instead.");
        return Str(j, "access_token") != null ? ToSession(j) : null; // signed in straight away when confirmation is off
    }

    public async Task<Session> ConfirmEmailAsync(string email, string code) =>
        ToSession(await SendAsync(HttpMethod.Post, "/auth/v1/verify", new { type = "signup", email, token = code.Trim() }));

    public Task ResendConfirmationAsync(string email) => SendAsync(HttpMethod.Post, "/auth/v1/resend", new { type = "signup", email });

    public async Task<Session> SignInAsync(string email, string password) =>
        ToSession(await SendAsync(HttpMethod.Post, "/auth/v1/token?grant_type=password", new { email, password }));

    public async Task<Session> RefreshAsync(string refreshToken)
    {
        try { return ToSession(await SendAsync(HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token", new { refresh_token = refreshToken })); }
        catch (FriendsException ex) when (!ex.Message.StartsWith("Couldn't reach")) { throw new FriendsException("Please sign in again.", signedOut: true); }
    }

    public async Task SignOutAsync(Session s)
    {
        try { await SendAsync(HttpMethod.Post, "/auth/v1/logout", null, s); } catch { /* signed out here anyway */ }
    }

    public Task SendPasswordResetAsync(string email) => SendAsync(HttpMethod.Post, "/auth/v1/recover", new { email });

    public async Task<Session> ResetPasswordAsync(string email, string code, string newPassword)
    {
        var s = ToSession(await SendAsync(HttpMethod.Post, "/auth/v1/verify", new { type = "recovery", email, token = code.Trim() }));
        await SendAsync(HttpMethod.Put, "/auth/v1/user", new { password = newPassword }, s);
        return s;
    }

    // ---- friends

    public async Task<string> MyUsernameAsync(Session s)
    {
        var j = await Rpc("me", null, s);
        return j is JsonArray { Count: > 0 } a ? Str(a[0], "username") ?? "" : "";
    }

    public Task HeartbeatAsync(Session s, string? game, string? gameTitle) => Rpc("heartbeat", new { game = game ?? "", game_title = gameTitle ?? "" }, s);

    public Task GoOfflineAsync(Session s) => Rpc("go_offline", null, s);

    private static DateTime? Time(JsonNode? n, string key) => Str(n, key) is { } t && DateTime.TryParse(t, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

    public async Task<List<Friend>> FriendsAsync(Session s)
    {
        var list = new List<Friend>();
        if (await Rpc("get_friends", null, s) is JsonArray a)
            foreach (var r in a)
                list.Add(new Friend(Str(r, "id") ?? "", Str(r, "username") ?? "", r?["online"]?.GetValue<bool>() == true,
                    Str(r, "playing"), Str(r, "playing_title"), Time(r, "last_seen"), Time(r, "since")));
        return list;
    }

    public async Task<List<FriendRequest>> RequestsAsync(Session s)
    {
        var list = new List<FriendRequest>();
        if (await Rpc("get_requests", null, s) is JsonArray a)
            foreach (var r in a)
                list.Add(new FriendRequest(Str(r, "id") ?? "", Str(r, "username") ?? "", Str(r, "direction") == "incoming", Time(r, "created_at") ?? DateTime.UtcNow));
        return list;
    }

    public async Task<string> SendRequestAsync(Session s, string username) =>
        (await Rpc("send_friend_request", new { target = username }, s))?.GetValue<string>() ?? "sent";

    public Task AnswerRequestAsync(Session s, string fromId, bool accept) => Rpc("answer_friend_request", new { requester = fromId, accept }, s);
    public Task CancelRequestAsync(Session s, string toId) => Rpc("cancel_friend_request", new { target = toId }, s);
    public Task RemoveFriendAsync(Session s, string friendId) => Rpc("remove_friend", new { friend = friendId }, s);
}
