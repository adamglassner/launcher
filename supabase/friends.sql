-- Ironite Launcher: Friends (accounts, friend requests, friends, online status)
--
-- Paste this whole file into Supabase: SQL Editor > New query > Run. It's safe to run again.
--
-- Everything the launcher does goes through the functions at the bottom (called as /rest/v1/rpc/<name>),
-- never straight at the tables, so a future Ironite-run service only has to offer the same functions.
-- The tables have row-level security on with no policies: nobody can read or change them directly.

create extension if not exists citext;

-- ---------------------------------------------------------------------------- tables

create table if not exists public.profiles (
  id          uuid primary key references auth.users (id) on delete cascade,
  username    citext not null unique check (username ~ '^[A-Za-z0-9_]{3,20}$'),
  created_at  timestamptz not null default now(),
  -- online status: the launcher checks in every minute while it's open
  last_seen   timestamptz,
  playing     text,          -- game id, e.g. 'solarsiege'
  playing_title text         -- e.g. 'Solar Siege'
);

create table if not exists public.friend_requests (
  from_id     uuid not null references public.profiles (id) on delete cascade,
  to_id       uuid not null references public.profiles (id) on delete cascade,
  created_at  timestamptz not null default now(),
  primary key (from_id, to_id),
  check (from_id <> to_id)
);
create index if not exists friend_requests_to on public.friend_requests (to_id);

-- one row per direction (a is friends with b, and b with a)
create table if not exists public.friends (
  user_id     uuid not null references public.profiles (id) on delete cascade,
  friend_id   uuid not null references public.profiles (id) on delete cascade,
  since       timestamptz not null default now(),
  primary key (user_id, friend_id),
  check (user_id <> friend_id)
);

alter table public.profiles        enable row level security;
alter table public.friend_requests enable row level security;
alter table public.friends         enable row level security;

-- ---------------------------------------------------------------------------- new accounts

-- Makes the profile when someone registers (the launcher sends the username with the sign-up).
create or replace function public.handle_new_user()
returns trigger language plpgsql security definer set search_path = public as $$
begin
  insert into public.profiles (id, username)
  values (new.id, new.raw_user_meta_data ->> 'username');
  return new;
end $$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();

-- ---------------------------------------------------------------------------- functions the launcher calls

-- Before registering: is this username free? (callable signed out)
create or replace function public.username_available(name text)
returns boolean language sql stable security definer set search_path = public as $$
  select name ~ '^[A-Za-z0-9_]{3,20}$'
     and not exists (select 1 from profiles where username = name::citext);
$$;

-- The signed-in player's own profile.
create or replace function public.me()
returns table (id uuid, username text)
language sql stable security definer set search_path = public as $$
  select p.id, p.username::text from profiles p where p.id = auth.uid();
$$;

-- Online status: called every minute while the launcher is open (and when a game starts or stops).
create or replace function public.heartbeat(game text default null, game_title text default null)
returns void language sql security definer set search_path = public as $$
  update profiles
     set last_seen = now(),
         playing = nullif(game, ''),
         playing_title = nullif(game_title, '')
   where id = auth.uid();
$$;

-- Called when the launcher closes or the player signs out.
create or replace function public.go_offline()
returns void language sql security definer set search_path = public as $$
  update profiles set last_seen = null, playing = null, playing_title = null where id = auth.uid();
$$;

-- get_friends() (the friends list, with who's online and what they're playing) is near the end of this file.

-- Friend requests sent to me ('incoming') and by me ('outgoing').
create or replace function public.get_requests()
returns table (id uuid, username text, direction text, created_at timestamptz)
language sql stable security definer set search_path = public as $$
  select p.id, p.username::text, 'incoming', r.created_at
    from friend_requests r join profiles p on p.id = r.from_id
   where r.to_id = auth.uid()
  union all
  select p.id, p.username::text, 'outgoing', r.created_at
    from friend_requests r join profiles p on p.id = r.to_id
   where r.from_id = auth.uid()
   order by 4 desc;
$$;

-- Send a friend request by username. If they had already asked me, we become friends straight away.
-- Returns 'sent', 'friends', 'already_friends' or 'already_sent'; raises a message the launcher shows.
create or replace function public.send_friend_request(target text)
returns text language plpgsql security definer set search_path = public as $$
declare
  me uuid := auth.uid();
  them uuid;
begin
  if me is null then raise exception 'Sign in first.'; end if;
  select id into them from profiles where username = target::citext;
  if them is null then raise exception 'There''s no player called %.', target; end if;
  if them = me then raise exception 'That''s you!'; end if;
  if exists (select 1 from friends where user_id = me and friend_id = them) then return 'already_friends'; end if;
  if exists (select 1 from friend_requests where from_id = them and to_id = me) then
    delete from friend_requests where from_id = them and to_id = me;
    insert into friends (user_id, friend_id) values (me, them), (them, me) on conflict do nothing;
    return 'friends';
  end if;
  if exists (select 1 from friend_requests where from_id = me and to_id = them) then return 'already_sent'; end if;
  if (select count(*) from friend_requests where from_id = me) >= 50 then
    raise exception 'You have 50 friend requests waiting. Cancel some first.';
  end if;
  insert into friend_requests (from_id, to_id) values (me, them);
  return 'sent';
end $$;

-- Accept or decline a request someone sent me.
create or replace function public.answer_friend_request(requester uuid, accept boolean)
returns void language plpgsql security definer set search_path = public as $$
declare me uuid := auth.uid();
begin
  if me is null then raise exception 'Sign in first.'; end if;
  delete from friend_requests where from_id = requester and to_id = me;
  if not found then raise exception 'That friend request is gone.'; end if;
  if accept then
    insert into friends (user_id, friend_id) values (me, requester), (requester, me) on conflict do nothing;
  end if;
end $$;

-- Take back a request I sent.
create or replace function public.cancel_friend_request(target uuid)
returns void language sql security definer set search_path = public as $$
  delete from friend_requests where from_id = auth.uid() and to_id = target;
$$;

-- Unfriend (both directions).
create or replace function public.remove_friend(friend uuid)
returns void language sql security definer set search_path = public as $$
  delete from friends
   where (user_id = auth.uid() and friend_id = friend)
      or (user_id = friend and friend_id = auth.uid());
$$;

-- ---------------------------------------------------------------------------- who may call what

revoke all on function public.handle_new_user() from public, anon, authenticated;
revoke all on function public.username_available(text), public.me(), public.heartbeat(text, text), public.go_offline(),
  public.get_requests(), public.send_friend_request(text), public.answer_friend_request(uuid, boolean),
  public.cancel_friend_request(uuid), public.remove_friend(uuid) from public;
grant execute on function public.username_available(text) to anon, authenticated;
grant execute on function public.me(), public.heartbeat(text, text), public.go_offline(),
  public.get_requests(), public.send_friend_request(text), public.answer_friend_request(uuid, boolean),
  public.cancel_friend_request(uuid), public.remove_friend(uuid) to authenticated;

-- ============================================================================ friends' games (launcher 3.5)
-- Each launcher shares a summary of its player's games with their friends: play time, when they last played,
-- and achievements. Players can turn sharing off in Settings.

alter table public.profiles add column if not exists share_games boolean not null default true;

create table if not exists public.game_library (
  user_id       uuid not null references public.profiles (id) on delete cascade,
  game_id       text not null,
  title         text not null,
  kind          text not null default 'ironite',   -- 'ironite' or 'own' (a game the player added)
  installed     boolean not null default false,
  play_seconds  bigint not null default 0,
  play_count    integer not null default 0,
  last_played   timestamptz,
  ach_unlocked  integer not null default 0,
  ach_total     integer not null default 0,
  achievements  jsonb,                             -- [{name, description, unlocked, at, icon}]
  updated_at    timestamptz not null default now(),
  primary key (user_id, game_id)
);
alter table public.game_library enable row level security;

-- Replaces the signed-in player's shared games with the launcher's current list.
create or replace function public.sync_my_games(games jsonb)
returns void language plpgsql security definer set search_path = public as $$
declare me uuid := auth.uid();
begin
  if me is null then raise exception 'Sign in first.'; end if;
  if jsonb_typeof(games) <> 'array' or jsonb_array_length(games) > 500 or length(games::text) > 2000000 then
    raise exception 'Too much to share.';
  end if;
  delete from game_library g
   where g.user_id = me
     and not exists (select 1 from jsonb_array_elements(games) x where x ->> 'id' = g.game_id);
  insert into game_library (user_id, game_id, title, kind, installed, play_seconds, play_count, last_played,
                            ach_unlocked, ach_total, achievements, updated_at)
  select me, left(x ->> 'id', 80), left(coalesce(x ->> 'title', x ->> 'id'), 80),
         case when x ->> 'kind' = 'own' then 'own' else 'ironite' end,
         coalesce((x ->> 'installed')::boolean, false),
         greatest(coalesce((x ->> 'playSeconds')::bigint, 0), 0),
         greatest(coalesce((x ->> 'playCount')::integer, 0), 0),
         case when coalesce((x ->> 'lastPlayed')::bigint, 0) > 0 then to_timestamp((x ->> 'lastPlayed')::bigint) end,
         greatest(coalesce((x ->> 'achUnlocked')::integer, 0), 0),
         greatest(coalesce((x ->> 'achTotal')::integer, 0), 0),
         x -> 'achievements', now()
    from jsonb_array_elements(games) x
   where coalesce(x ->> 'id', '') <> ''
  on conflict (user_id, game_id) do update set
    title = excluded.title, kind = excluded.kind, installed = excluded.installed,
    play_seconds = excluded.play_seconds, play_count = excluded.play_count, last_played = excluded.last_played,
    ach_unlocked = excluded.ach_unlocked, ach_total = excluded.ach_total, achievements = excluded.achievements,
    updated_at = now();
end $$;

create or replace function public.my_share_games()
returns boolean language sql stable security definer set search_path = public as $$
  select coalesce((select share_games from profiles where id = auth.uid()), true);
$$;

-- Turning sharing off also removes what was shared.
create or replace function public.set_share_games(share boolean)
returns void language plpgsql security definer set search_path = public as $$
begin
  update profiles set share_games = share where id = auth.uid();
  if not share then delete from game_library where user_id = auth.uid(); end if;
end $$;

-- A friend's shared games (nothing if they're not your friend or don't share).
create or replace function public.get_friend_games(friend uuid)
returns table (game_id text, title text, kind text, installed boolean, play_seconds bigint, play_count integer,
               last_played timestamptz, ach_unlocked integer, ach_total integer, achievements jsonb, shared boolean)
language plpgsql stable security definer set search_path = public as $$
begin
  if not exists (select 1 from friends f where f.user_id = auth.uid() and f.friend_id = friend) then
    raise exception 'You can only see your friends'' games.';
  end if;
  if not (select p.share_games from profiles p where p.id = friend) then
    return query select null::text, null::text, null::text, null::boolean, null::bigint, null::integer,
                        null::timestamptz, null::integer, null::integer, null::jsonb, false;
    return;
  end if;
  return query
    select g.game_id, g.title, g.kind, g.installed, g.play_seconds, g.play_count, g.last_played,
           g.ach_unlocked, g.ach_total, g.achievements, true
      from game_library g
     where g.user_id = friend
     order by g.last_played desc nulls last, g.play_seconds desc;
end $$;

revoke all on function public.sync_my_games(jsonb), public.my_share_games(), public.set_share_games(boolean),
  public.get_friend_games(uuid) from public;
grant execute on function public.sync_my_games(jsonb), public.my_share_games(), public.set_share_games(boolean),
  public.get_friend_games(uuid) to authenticated;

-- ============================================================================ profile customisation (launcher 3.5)
-- A profile picture (a small JPEG, stored as text), a theme colour and a favourite game.

alter table public.profiles add column if not exists avatar text check (avatar is null or length(avatar) <= 120000);
alter table public.profiles add column if not exists avatar_at timestamptz;
alter table public.profiles add column if not exists theme text not null default 'ember'
  check (theme in ('ember', 'ocean', 'forest', 'violet', 'rose', 'gold', 'slate'));
alter table public.profiles add column if not exists favorite_game text check (favorite_game is null or length(favorite_game) <= 80);
alter table public.profiles add column if not exists favorite_title text check (favorite_title is null or length(favorite_title) <= 80);

-- The friends list: whether each friend is online (checked in within the last 2 minutes), what they're playing,
-- their theme, and when their picture last changed (the launcher fetches pictures separately, only when they change).
drop function if exists public.get_friends();
create function public.get_friends()
returns table (id uuid, username text, online boolean, playing text, playing_title text, last_seen timestamptz, since timestamptz,
               theme text, avatar_at timestamptz)
language sql stable security definer set search_path = public as $$
  select p.id, p.username::text,
         coalesce(p.last_seen > now() - interval '2 minutes', false) as online,
         case when p.last_seen > now() - interval '2 minutes' then p.playing end,
         case when p.last_seen > now() - interval '2 minutes' then p.playing_title end,
         p.last_seen, f.since, p.theme, p.avatar_at
    from friends f join profiles p on p.id = f.friend_id
   where f.user_id = auth.uid()
   order by online desc, lower(p.username::text);
$$;

-- A profile: your own, or a friend's.
create or replace function public.get_profile(who uuid)
returns table (id uuid, username text, theme text, favorite_game text, favorite_title text, avatar_at timestamptz, share_games boolean, created_at timestamptz)
language plpgsql stable security definer set search_path = public as $$
begin
  if who <> auth.uid() and not exists (select 1 from friends f where f.user_id = auth.uid() and f.friend_id = who) then
    raise exception 'You can only see your friends'' profiles.';
  end if;
  return query select p.id, p.username::text, p.theme, p.favorite_game, p.favorite_title, p.avatar_at, p.share_games, p.created_at
                 from profiles p where p.id = who;
end $$;

-- Profile pictures (yours or friends'), only for the ids asked.
create or replace function public.get_avatars(ids uuid[])
returns table (id uuid, avatar text, avatar_at timestamptz)
language sql stable security definer set search_path = public as $$
  select p.id, p.avatar, p.avatar_at from profiles p
   where p.id = any(ids) and p.avatar is not null
     and (p.id = auth.uid() or exists (select 1 from friends f where f.user_id = auth.uid() and f.friend_id = p.id));
$$;

-- Saves your profile. The picture only changes when set_avatar is true (null removes it).
create or replace function public.set_my_profile(new_theme text, fav_game text, fav_title text, set_avatar boolean default false, new_avatar text default null)
returns void language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'Sign in first.'; end if;
  update profiles
     set theme = coalesce(nullif(new_theme, ''), theme),
         favorite_game = nullif(fav_game, ''),
         favorite_title = nullif(fav_title, ''),
         avatar = case when set_avatar then nullif(new_avatar, '') else avatar end,
         avatar_at = case when set_avatar then (case when nullif(new_avatar, '') is null then null else now() end) else avatar_at end
   where id = auth.uid();
end $$;

revoke all on function public.get_friends(), public.get_profile(uuid), public.get_avatars(uuid[]),
  public.set_my_profile(text, text, text, boolean, text) from public;
grant execute on function public.get_friends(), public.get_profile(uuid), public.get_avatars(uuid[]),
  public.set_my_profile(text, text, text, boolean, text) to authenticated;
