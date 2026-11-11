namespace Ironite.Launcher.Services.Friends;

// The launcher only talks to Friends through IFriendsBackend. Today that's Supabase (SupabaseBackend);
// moving to an Ironite-run service later means writing one more class with these same calls.

public sealed record Session(string AccessToken, string RefreshToken, DateTime ExpiresAt, string UserId, string Email);

public sealed record Friend(string Id, string Username, bool Online, string? Playing, string? PlayingTitle, DateTime? LastSeen, DateTime? Since);

public sealed record FriendRequest(string Id, string Username, bool Incoming, DateTime CreatedAt);

/// <summary>What the server said went wrong, in words the player can read.</summary>
public sealed class FriendsException(string message, bool signedOut = false) : Exception(message)
{
    /// <summary>The session is no longer valid: the player needs to sign in again.</summary>
    public bool SignedOut { get; } = signedOut;
}

public interface IFriendsBackend
{
    // ---- accounts
    Task<bool> UsernameAvailableAsync(string username);
    /// <summary>
    /// Registers. Returns the session straight away when the server doesn't ask for email confirmation;
    /// otherwise null, and the server emails a 6-digit code to confirm the address.
    /// </summary>
    Task<Session?> SignUpAsync(string email, string password, string username);
    Task<Session> ConfirmEmailAsync(string email, string code);
    Task ResendConfirmationAsync(string email);
    Task<Session> SignInAsync(string email, string password);
    Task<Session> RefreshAsync(string refreshToken);
    Task SignOutAsync(Session s);
    /// <summary>Emails a 6-digit code for choosing a new password.</summary>
    Task SendPasswordResetAsync(string email);
    Task<Session> ResetPasswordAsync(string email, string code, string newPassword);

    // ---- friends (all need a session)
    Task<string> MyUsernameAsync(Session s);
    Task HeartbeatAsync(Session s, string? game, string? gameTitle);
    Task GoOfflineAsync(Session s);
    Task<List<Friend>> FriendsAsync(Session s);
    Task<List<FriendRequest>> RequestsAsync(Session s);
    /// <summary>Returns "sent", "friends" (they'd already asked you), "already_friends" or "already_sent".</summary>
    Task<string> SendRequestAsync(Session s, string username);
    Task AnswerRequestAsync(Session s, string fromId, bool accept);
    Task CancelRequestAsync(Session s, string toId);
    Task RemoveFriendAsync(Session s, string friendId);
}
