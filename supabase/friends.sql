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

-- Friends, with whether they're online (checked in within the last 2 minutes) and what they're playing.
create or replace function public.get_friends()
returns table (id uuid, username text, online boolean, playing text, playing_title text, last_seen timestamptz, since timestamptz)
language sql stable security definer set search_path = public as $$
  select p.id, p.username::text,
         coalesce(p.last_seen > now() - interval '2 minutes', false) as online,
         case when p.last_seen > now() - interval '2 minutes' then p.playing end,
         case when p.last_seen > now() - interval '2 minutes' then p.playing_title end,
         p.last_seen, f.since
    from friends f join profiles p on p.id = f.friend_id
   where f.user_id = auth.uid()
   order by online desc, lower(p.username::text);
$$;

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
  public.get_friends(), public.get_requests(), public.send_friend_request(text), public.answer_friend_request(uuid, boolean),
  public.cancel_friend_request(uuid), public.remove_friend(uuid) from public;
grant execute on function public.username_available(text) to anon, authenticated;
grant execute on function public.me(), public.heartbeat(text, text), public.go_offline(), public.get_friends(),
  public.get_requests(), public.send_friend_request(text), public.answer_friend_request(uuid, boolean),
  public.cancel_friend_request(uuid), public.remove_friend(uuid) to authenticated;
