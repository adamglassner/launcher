using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ironite.Launcher.Services;
using Ironite.Launcher.Services.Friends;
using Ironite.Launcher.Views;
using static Ironite.Launcher.Views.Ui;

namespace Ironite.Launcher;

/// <summary>The Friends page: register / sign in, add friends by username, requests, and who's online.</summary>
public partial class MainWindow
{
    private static FriendsService Fr => App.Friends;

    // Which form is showing when signed out: signin, register, confirm, forgot, reset.
    private string _fMode = "signin";
    private string _fEmail = "";
    private string _fNote = "";
    private bool _fBusy;
    // Kept between redraws so typing isn't lost when the friends list refreshes.
    private readonly TextBox _addBox = new() { MaxLength = 20 };
    private string _addNote = "";
    private bool _addNoteOk;

    private void UpdateFriendsBadge()
    {
        if (_friendsBadge == null) return;
        string text, bg, fg, line;
        string? tip = null;
        if (!Fr.Available) { text = Track("SOON"); bg = "#0FFFFFFF"; fg = "#9a9fab"; line = "#2f3542"; }
        else if (Fr.Requests.Count(r => r.Incoming) is > 0 and var n)
        {
            text = n.ToString(); bg = "#2EE7C35A"; fg = "#f1d78a"; line = "#80E7C35A";
            tip = $"{n} friend request{(n > 1 ? "s" : "")}";
        }
        else if (Fr.Friends.Count(f => f.Online) is > 0 and var on)
        {
            text = on.ToString(); bg = "#2E5FC489"; fg = "#8fe0b0"; line = "#665FC489";
            tip = $"{on} friend{(on > 1 ? "s" : "")} online";
        }
        else { _friendsBadge.Visibility = Visibility.Collapsed; return; }
        _friendsBadge.Visibility = Visibility.Visible;
        _friendsBadge.Background = B(bg);
        _friendsBadge.BorderBrush = B(line);
        _friendsBadge.ToolTip = tip;
        _friendsBadge.Child = new TextBlock { Text = text, FontSize = Fr.Available ? 11 : 9, FontWeight = FontWeights.Bold, FontFamily = Display, Foreground = B(fg), TextAlignment = TextAlignment.Center };
    }

    private void RenderFriends()
    {
        UpdateFriendsBadge();
        if (!Fr.Available) { RenderFriendsSoon(); return; }
        if (!Fr.SignedIn)
        {
            PageStart("Friends", "Play Ironite games with the people you know. Sign in to see your friends and what they’re playing.");
            if (!Fr.Loaded)
            {
                _page.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0), Children = { Spinner(), Muted("Signing in…") } });
                return;
            }
            _page.Children.Add(AccountForm());
            return;
        }

        PageStart("Friends", null);
        var who = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        who.Children.Add(Muted("Signed in as ", 14, TextWrapping.NoWrap));
        who.Children.Add(T(Fr.Username, 14, weight: FontWeights.SemiBold));
        who.Children.Add(Muted("  ·  ", 14, TextWrapping.NoWrap));
        who.Children.Add(Link("Sign out", async () =>
        {
            await Fr.SignOutAsync();
            _fMode = "signin";
            Refresh(force: true);
        }, 13));
        _page.Children.Add(who);
        if (Fr.Error != null) _page.Children.Add(Box(T(Fr.Error, 13, B("#f0b1aa"), wrap: TextWrapping.Wrap), true).Also(b => { b.Margin = new Thickness(0, 0, 0, 14); b.MaxWidth = 760; b.HorizontalAlignment = HorizontalAlignment.Left; }));

        _page.Children.Add(AddFriendCard());

        var incoming = Fr.Requests.Where(r => r.Incoming).ToList();
        var outgoing = Fr.Requests.Where(r => !r.Incoming).ToList();
        if (incoming.Count > 0)
        {
            _page.Children.Add(FriendsHead("FRIEND REQUESTS", incoming.Count));
            foreach (var r in incoming)
                _page.Children.Add(PersonRow(r.Username, T($"Wants to be friends · {Ui.Ago(new DateTimeOffset(r.CreatedAt).ToUnixTimeSeconds()).ToLowerInvariant()}", 12.5, Res("Muted")),
                    Ui.Play("Accept", "inst", () => Do(() => Fr.AnswerAsync(r.Id, true), $"You and {r.Username} are friends now."), small: true).Also(b => { b.Padding = new Thickness(14, 7, 14, 7); b.FontSize = 13; }),
                    Btn("Decline", "GhostButton", () => Do(() => Fr.AnswerAsync(r.Id, false)), small: true).Also(b => { b.Padding = new Thickness(14, 7, 14, 7); b.FontSize = 13; })));
        }

        var online = Fr.Friends.Count(f => f.Online);
        _page.Children.Add(FriendsHead("FRIENDS", Fr.Friends.Count, Fr.Friends.Count > 0 ? $"{online} online" : null));
        if (Fr.Friends.Count == 0)
            _page.Children.Add(Muted("No friends yet. Add someone by their username above. When they accept, you’ll see when they’re online and what they’re playing.", 13.5)
                .Also(t => { t.MaxWidth = 760; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 0, 0, 18); }));
        foreach (var f in Fr.Friends)
        {
            UIElement status = f.Online && f.PlayingTitle != null
                ? T("Playing " + f.PlayingTitle, 12.5, B("#8fe0b0"), FontWeights.SemiBold)
                : f.Online ? T("Online", 12.5, B("#8fe0b0"))
                : T(f.LastSeen is { } ls ? "Last online " + LastOnline(ls) : "Offline", 12.5, Res("Muted"));
            var friend = f;
            _page.Children.Add(PersonRow(f.Username, status, null,
                Link("Remove", () => ConfirmRemoveFriend(friend)), online: f.Online));
        }

        if (outgoing.Count > 0)
        {
            _page.Children.Add(FriendsHead("SENT", outgoing.Count));
            foreach (var r in outgoing)
                _page.Children.Add(PersonRow(r.Username, T("Waiting for them to accept", 12.5, Res("Muted")), null,
                    Link("Cancel", () => Do(() => Fr.CancelAsync(r.Id)))));
        }
    }

    private static string LastOnline(DateTime utc)
    {
        var s = (DateTime.UtcNow - utc).TotalSeconds;
        if (s < 3600) return Math.Max(1, (int)(s / 60)) + " min ago";
        if (s < 86400) return (int)(s / 3600) + " h ago";
        return Ui.Ago(new DateTimeOffset(utc).ToUnixTimeSeconds()).ToLowerInvariant();
    }

    private static TextBlock FriendsHead(string text, int count, string? extra = null) =>
        T(Track(text, 2) + $"   {count}" + (extra != null ? $" · {extra}" : ""), 12, Res("Muted"), FontWeights.Bold, true)
            .Also(t => t.Margin = new Thickness(0, 22, 0, 10));

    /// <summary>A round avatar with the player's initial, coloured from their name.</summary>
    private static FrameworkElement Avatar(string name, bool? online)
    {
        var h = 0;
        foreach (var ch in name.ToLowerInvariant()) h = (h * 31 + ch) % 360;
        var g = new Grid { Width = 40, Height = 40 };
        g.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(20),
            Background = new LinearGradientBrush(Hsl(h, 0.5, 0.45), Hsl(h + 35, 0.45, 0.25), 45),
            Child = new TextBlock
            {
                Text = name.Length > 0 ? char.ToUpper(name[0]).ToString() : "?", FontSize = 17, FontWeight = FontWeights.Bold, FontFamily = Display,
                Foreground = B("#F2FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        });
        if (online != null)
            g.Children.Add(new Border
            {
                Width = 13, Height = 13, CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Background = online == true ? Res("Ok") : B("#5a606c"), BorderBrush = Res("Panel"), BorderThickness = new Thickness(2.5),
            });
        return g;
    }

    private static Border PersonRow(string name, UIElement status, UIElement? action1, UIElement? action2, bool? online = null)
    {
        var row = new DockPanel();
        var av = Avatar(name, online);
        av.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(av, Dock.Left);
        row.Children.Add(av);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (action1 != null) { if (action1 is FrameworkElement a) a.Margin = new Thickness(0, 0, 8, 0); actions.Children.Add(action1); }
        if (action2 != null) actions.Children.Add(action2);
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(T(name, 15, weight: FontWeights.SemiBold, display: true));
        if (status is FrameworkElement fe) fe.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(status);
        row.Children.Add(text);
        return new Border
        {
            Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 16, 10), Margin = new Thickness(0, 0, 0, 8), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch, Child = row,
        };
    }

    private UIElement AddFriendCard()
    {
        (_addBox.Parent as Panel)?.Children.Remove(_addBox);
        var send = Ui.Play("+  Send request", "", AddFriend, small: true);
        send.Padding = new Thickness(16, 8, 16, 8);
        send.FontSize = 13.5;
        send.Margin = new Thickness(10, 0, 0, 0);
        _addBox.KeyDown -= AddBoxKey;
        _addBox.KeyDown += AddBoxKey;
        var row = new DockPanel();
        DockPanel.SetDock(send, Dock.Right);
        row.Children.Add(send);
        row.Children.Add(Placeholder(_addBox, "Their username"));
        var p = new StackPanel();
        p.Children.Add(T("Add a friend", 15, weight: FontWeights.SemiBold, display: true).Also(t => t.Margin = new Thickness(0, 0, 0, 8)));
        p.Children.Add(row);
        p.Children.Add(T(_addNote.Length > 0 ? _addNote : $"Your username is {Fr.Username}. Share it so friends can add you.", 12.5,
            _addNote.Length == 0 ? Res("Muted") : _addNoteOk ? B("#8fe0b0") : B("#f0b1aa"), wrap: TextWrapping.Wrap).Also(t => t.Margin = new Thickness(0, 8, 0, 0)));
        return new Border
        {
            Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 14), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch, Child = p,
        };
    }

    private void AddBoxKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { AddFriend(); e.Handled = true; }
    }

    private async void AddFriend()
    {
        var name = _addBox.Text.Trim();
        try
        {
            var r = await Fr.SendRequestAsync(name);
            (_addNote, _addNoteOk) = r switch
            {
                "friends" => ($"{name} had already asked to be your friend, so you’re friends now.", true),
                "already_friends" => ($"You’re already friends with {name}.", true),
                "already_sent" => ($"You’ve already asked {name}. They need to accept.", true),
                _ => ($"Friend request sent to {name}.", true),
            };
            _addBox.Text = "";
        }
        catch (FriendsException ex) { (_addNote, _addNoteOk) = (ex.Message, false); }
        RenderFriends();
        _addBox.Focus();
    }

    private async void Do(Func<Task> action, string? done = null)
    {
        try
        {
            await action();
            if (done != null) ShowToast(done);
        }
        catch (FriendsException ex) { ShowToast(ex.Message); }
        RenderFriends();
    }

    private void ConfirmRemoveFriend(Friend f)
    {
        _modalId = null;
        OpenModal(new Modal
        {
            Pill = Pill("FRIENDS", "mine"), Title = DlgTitle($"Remove {f.Username} from your friends?"),
            Desc = DlgDesc($"You won’t see when {f.Username} is online any more, and they won’t see you. You can add each other again any time."),
            Actions = Actions(Btn("Remove", "DangerButton", () => { CloseModal(); Do(() => Fr.RemoveAsync(f.Id), $"{f.Username} was removed from your friends."); }),
                Btn("Cancel", "GhostButton", CloseModal, small: true)),
        });
    }

    // ---------------------------------------------------------------- signed out: register / sign in

    private UIElement AccountForm()
    {
        var p = new StackPanel();
        var errBox = FormError(out var errText);
        var err = ErrorSetter(errBox, errText);
        var email = new TextBox { Text = _fEmail, MaxLength = 254 };
        var password = new PasswordBox { MaxLength = 128 };
        var password2 = new PasswordBox { MaxLength = 128 };
        var username = new TextBox { MaxLength = 20 };
        var code = new TextBox { MaxLength = 10, FontSize = 18, HorizontalContentAlignment = HorizontalAlignment.Center };
        var userNote = T("3 to 20 letters, numbers or underscores. Friends find you by it.", 12, Res("Muted"), wrap: TextWrapping.Wrap).Also(t => t.Margin = new Thickness(0, 5, 0, 0));

        async void Run(Func<Task> work)
        {
            if (_fBusy) return;
            _fBusy = true;
            err("");
            try { await work(); }
            catch (FriendsException ex) { err(ex.Message); }
            catch (Exception ex) { err(ex.Message); }
            finally { _fBusy = false; }
        }
        void Go(string mode, string note = "") { _fMode = mode; _fNote = note; RenderFriends(); }

        Button Primary(string text, Action click)
        {
            var b = Ui.Play(text, "", click, small: true);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.Margin = new Thickness(0, 4, 0, 12);
            return b;
        }
        void Submit(Control c, Action a) => c.KeyDown += (_, e) => { if (e.Key == Key.Enter) { a(); e.Handled = true; } };

        string title, sub;
        switch (_fMode)
        {
            case "register":
                title = "Create your Ironite account";
                sub = "Friends find you by your username.";
                p.Children.Add(Field("Email", email));
                p.Children.Add(Field("Username", Stack(username, userNote)));
                p.Children.Add(Field("Password", password, "At least 8 characters."));
                p.Children.Add(Field("Password again", password2));
                p.Children.Add(errBox);
                Action reg = () => Run(async () =>
                {
                    if (password.Password != password2.Password) throw new FriendsException("The passwords don’t match.");
                    _fEmail = email.Text.Trim();
                    if (await Fr.SignUpAsync(email.Text, password.Password, username.Text.Trim()))
                    {
                        ShowToast($"Welcome to Ironite, {Fr.Username}!");
                        Go("signin");
                        return;
                    }
                    Go("confirm", $"We sent a 6-digit code to {_fEmail}. It can take a minute; check your spam folder too.");
                });
                Submit(password2, reg);
                p.Children.Add(Primary("Create account", reg));
                p.Children.Add(Row(4, Muted("Already have an account? ", 13, TextWrapping.NoWrap), Link("Sign in", () => Go("signin"), 13)));
                username.LostFocus += async (_, _) =>
                {
                    var u = username.Text.Trim();
                    if (u.Length == 0) return;
                    if (FriendsService.CheckUsername(u) is { } bad) { userNote.Text = bad; userNote.Foreground = B("#f0b1aa"); return; }
                    try
                    {
                        var free = await Fr.UsernameAvailableAsync(u);
                        userNote.Text = free ? $"{u} is available." : $"{u} is taken. Choose another.";
                        userNote.Foreground = free ? B("#8fe0b0") : B("#f0b1aa");
                    }
                    catch { /* checked again when creating the account */ }
                };
                break;

            case "confirm":
                title = "Confirm your email";
                sub = _fNote.Length > 0 ? _fNote : $"Enter the 6-digit code we emailed to {_fEmail}.";
                p.Children.Add(Field("Code", code));
                p.Children.Add(errBox);
                Action conf = () => Run(async () =>
                {
                    await Fr.ConfirmEmailAsync(_fEmail, code.Text);
                    ShowToast($"Welcome to Ironite, {Fr.Username}!");
                    Go("signin");
                });
                Submit(code, conf);
                p.Children.Add(Primary("Confirm", conf));
                p.Children.Add(Row(16, Link("Send a new code", () => Run(async () => { await Fr.ResendConfirmationAsync(_fEmail); err(""); ShowToast("A new code is on its way."); }), 13),
                    Link("Use a different email", () => Go("register"), 13)));
                break;

            case "forgot":
                title = "Forgot your password?";
                sub = "We’ll email you a code to choose a new one.";
                p.Children.Add(Field("Email", email));
                p.Children.Add(errBox);
                Action forgot = () => Run(async () =>
                {
                    await Fr.SendPasswordResetAsync(email.Text);
                    _fEmail = email.Text.Trim();
                    Go("reset", $"If there’s an account for {_fEmail}, we sent it a 6-digit code.");
                });
                Submit(email, forgot);
                p.Children.Add(Primary("Email me a code", forgot));
                p.Children.Add(Link("Back to sign in", () => Go("signin"), 13));
                break;

            case "reset":
                title = "Choose a new password";
                sub = _fNote;
                p.Children.Add(Field("Code", code));
                p.Children.Add(Field("New password", password, "At least 8 characters."));
                p.Children.Add(errBox);
                Action reset = () => Run(async () =>
                {
                    await Fr.ResetPasswordAsync(_fEmail, code.Text, password.Password);
                    ShowToast("Your password was changed.");
                    Go("signin");
                });
                Submit(password, reset);
                p.Children.Add(Primary("Change password", reset));
                p.Children.Add(Link("Back to sign in", () => Go("signin"), 13));
                break;

            default:
                title = "Sign in";
                sub = "With your Ironite account.";
                p.Children.Add(Field("Email", email));
                p.Children.Add(Field("Password", password));
                p.Children.Add(errBox);
                Action signin = () => Run(async () =>
                {
                    _fEmail = email.Text.Trim();
                    try { await Fr.SignInAsync(email.Text, password.Password); }
                    catch (FriendsException ex) when (ex.Message.StartsWith("Confirm your email"))
                    {
                        await Fr.ResendConfirmationAsync(_fEmail);
                        Go("confirm", $"Confirm your email first. We sent a new 6-digit code to {_fEmail}.");
                        return;
                    }
                    Go("signin");
                });
                Submit(password, signin);
                p.Children.Add(Primary("Sign in", signin));
                p.Children.Add(Row(4, Muted("New to Ironite? ", 13, TextWrapping.NoWrap), Link("Create an account", () => Go("register"), 13)));
                // "Forgot password?" (Go("forgot")) comes back once the Friends server can send emails.
                break;
        }

        var card = new StackPanel();
        card.Children.Add(T(title, 22, weight: FontWeights.Bold, display: true));
        if (sub.Length > 0) card.Children.Add(Muted(sub, 13.5).Also(t => t.Margin = new Thickness(0, 4, 0, 18)));
        card.Children.Add(p);
        Dispatcher.BeginInvoke(() => (_fMode is "confirm" or "reset" ? code : _fMode == "register" || email.Text.Length == 0 ? email : password).Focus(),
            System.Windows.Threading.DispatcherPriority.Input);
        return new Border
        {
            Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(26, 22, 26, 14), Margin = new Thickness(0, 22, 0, 0), Width = 440, HorizontalAlignment = HorizontalAlignment.Left, Child = card,
        };
    }

    // ---------------------------------------------------------------- no Friends server yet

    private void RenderFriendsSoon()
    {
        PageStart("Friends", "Play Ironite games with the people you know.", Pill("COMING SOON", "soon"));
        var grid = new AutoGrid { MinItemWidth = 210, Gap = 14, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 22, 0, 22) };
        foreach (var (t, d) in new[] { ("Friends list", "Add friends and see who's online."), ("Now playing", "See which games your friends are playing right now."), ("Invites", "Invite a friend to a game in one click.") })
            grid.Children.Add(new Border
            {
                BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 16, 18, 16),
                Background = B("#06FFFFFF"), Opacity = 0.75,
                Child = new StackPanel { Children = { T(t, 15, weight: FontWeights.SemiBold, display: true), Muted(d, 13).Also(x => x.Margin = new Thickness(0, 4, 0, 0)) } },
            });
        _page.Children.Add(grid);
        _page.Children.Add(Empty("Coming soon", "Friends isn't available yet. It will arrive in a future launcher update, and the launcher updates itself, so there's nothing you need to do."));
    }
}
