# Unbound Rockhound — Phase 2 shared forum
# Apply via MCP apply_migration / Dashboard / supabase db push

-- ---------------------------------------------------------------------------
-- Categories (admin-defined seed; no client create for now)
-- ---------------------------------------------------------------------------
create table if not exists public.forum_categories (
  id uuid primary key,
  slug text not null unique,
  name text not null,
  description text not null default '',
  icon_key text not null default 'forum',
  sort_order int not null default 0,
  is_region_scoped boolean not null default false,
  created_at timestamptz not null default now(),
  constraint forum_categories_name_len check (char_length(name) between 1 and 80),
  constraint forum_categories_slug_format check (slug ~ '^[a-z0-9-]{2,48}$')
);

-- ---------------------------------------------------------------------------
-- Threads
-- ---------------------------------------------------------------------------
create table if not exists public.forum_threads (
  id uuid primary key default gen_random_uuid(),
  category_id uuid not null references public.forum_categories (id) on delete restrict,
  author_id uuid not null references public.profiles (id) on delete cascade,
  title text not null,
  body text not null default '',
  region_tag text,
  image_urls text[] not null default '{}',
  reply_count int not null default 0,
  like_count int not null default 0,
  is_pinned boolean not null default false,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint forum_threads_title_len check (char_length(title) between 1 and 200),
  constraint forum_threads_body_len check (char_length(body) <= 20000),
  constraint forum_threads_region_len check (region_tag is null or char_length(region_tag) <= 80),
  constraint forum_threads_images_cap check (cardinality(image_urls) <= 8)
);

create index if not exists forum_threads_category_updated_idx
  on public.forum_threads (category_id, updated_at desc);
create index if not exists forum_threads_author_idx on public.forum_threads (author_id);
create index if not exists forum_threads_region_idx on public.forum_threads (region_tag);

create or replace function public.set_forum_threads_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

drop trigger if exists forum_threads_set_updated_at on public.forum_threads;
create trigger forum_threads_set_updated_at
  before update on public.forum_threads
  for each row execute function public.set_forum_threads_updated_at();

-- ---------------------------------------------------------------------------
-- Posts (replies)
-- ---------------------------------------------------------------------------
create table if not exists public.forum_posts (
  id uuid primary key default gen_random_uuid(),
  thread_id uuid not null references public.forum_threads (id) on delete cascade,
  author_id uuid not null references public.profiles (id) on delete cascade,
  body text not null,
  image_urls text[] not null default '{}',
  like_count int not null default 0,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint forum_posts_body_len check (char_length(body) between 1 and 10000),
  constraint forum_posts_images_cap check (cardinality(image_urls) <= 8)
);

create index if not exists forum_posts_thread_created_idx
  on public.forum_posts (thread_id, created_at);

create or replace function public.set_forum_posts_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

drop trigger if exists forum_posts_set_updated_at on public.forum_posts;
create trigger forum_posts_set_updated_at
  before update on public.forum_posts
  for each row execute function public.set_forum_posts_updated_at();

create or replace function public.forum_posts_adjust_thread_counts()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if tg_op = 'INSERT' then
    update public.forum_threads
      set reply_count = reply_count + 1,
          updated_at = now()
      where id = new.thread_id;
    return new;
  elsif tg_op = 'DELETE' then
    update public.forum_threads
      set reply_count = greatest(reply_count - 1, 0),
          updated_at = now()
      where id = old.thread_id;
    return old;
  end if;
  return null;
end;
$$;

drop trigger if exists forum_posts_counts on public.forum_posts;
create trigger forum_posts_counts
  after insert or delete on public.forum_posts
  for each row execute function public.forum_posts_adjust_thread_counts();

-- ---------------------------------------------------------------------------
-- Likes (thread XOR post)
-- ---------------------------------------------------------------------------
create table if not exists public.forum_likes (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.profiles (id) on delete cascade,
  thread_id uuid references public.forum_threads (id) on delete cascade,
  post_id uuid references public.forum_posts (id) on delete cascade,
  created_at timestamptz not null default now(),
  constraint forum_likes_one_target check (
    (thread_id is not null and post_id is null)
    or (thread_id is null and post_id is not null)
  )
);

create unique index if not exists forum_likes_user_thread_uidx
  on public.forum_likes (user_id, thread_id) where thread_id is not null;
create unique index if not exists forum_likes_user_post_uidx
  on public.forum_likes (user_id, post_id) where post_id is not null;

create or replace function public.forum_likes_adjust_counts()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if tg_op = 'INSERT' then
    if new.thread_id is not null then
      update public.forum_threads set like_count = like_count + 1 where id = new.thread_id;
    elsif new.post_id is not null then
      update public.forum_posts set like_count = like_count + 1 where id = new.post_id;
    end if;
    return new;
  elsif tg_op = 'DELETE' then
    if old.thread_id is not null then
      update public.forum_threads set like_count = greatest(like_count - 1, 0) where id = old.thread_id;
    elsif old.post_id is not null then
      update public.forum_posts set like_count = greatest(like_count - 1, 0) where id = old.post_id;
    end if;
    return old;
  end if;
  return null;
end;
$$;

drop trigger if exists forum_likes_counts on public.forum_likes;
create trigger forum_likes_counts
  after insert or delete on public.forum_likes
  for each row execute function public.forum_likes_adjust_counts();

-- ---------------------------------------------------------------------------
-- Seed categories (stable UUIDs for clients)
-- ---------------------------------------------------------------------------
insert into public.forum_categories (id, slug, name, description, icon_key, sort_order, is_region_scoped)
values
  ('11111111-1111-4111-8111-111111111101', 'world-news', 'World News',
   'International rockhounding, geology, and mineral news.', 'globe', 10, false),
  ('11111111-1111-4111-8111-111111111102', 'usa-news', 'USA News',
   'United States mineral collecting and geology news.', 'flag', 20, false),
  ('11111111-1111-4111-8111-111111111103', 'local', 'Local',
   'Region-tagged discussion. Filtered by your home region when set.', 'location', 30, true),
  ('11111111-1111-4111-8111-111111111104', 'general', 'General Discussion',
   'Open conversation for the Unbound Rockhound community.', 'chat', 40, false)
on conflict (id) do update set
  slug = excluded.slug,
  name = excluded.name,
  description = excluded.description,
  icon_key = excluded.icon_key,
  sort_order = excluded.sort_order,
  is_region_scoped = excluded.is_region_scoped;

-- ---------------------------------------------------------------------------
-- RLS
-- ---------------------------------------------------------------------------
alter table public.forum_categories enable row level security;
alter table public.forum_threads enable row level security;
alter table public.forum_posts enable row level security;
alter table public.forum_likes enable row level security;

drop policy if exists "forum_categories_select_authenticated" on public.forum_categories;
create policy "forum_categories_select_authenticated"
  on public.forum_categories for select to authenticated using (true);

-- Categories are admin/migration-defined only (no insert/update/delete for clients)

drop policy if exists "forum_threads_select_authenticated" on public.forum_threads;
create policy "forum_threads_select_authenticated"
  on public.forum_threads for select to authenticated using (true);

drop policy if exists "forum_threads_insert_own" on public.forum_threads;
create policy "forum_threads_insert_own"
  on public.forum_threads for insert to authenticated
  with check (auth.uid() = author_id);

drop policy if exists "forum_threads_update_own" on public.forum_threads;
create policy "forum_threads_update_own"
  on public.forum_threads for update to authenticated
  using (auth.uid() = author_id)
  with check (auth.uid() = author_id);

drop policy if exists "forum_threads_delete_own" on public.forum_threads;
create policy "forum_threads_delete_own"
  on public.forum_threads for delete to authenticated
  using (auth.uid() = author_id);

drop policy if exists "forum_posts_select_authenticated" on public.forum_posts;
create policy "forum_posts_select_authenticated"
  on public.forum_posts for select to authenticated using (true);

drop policy if exists "forum_posts_insert_own" on public.forum_posts;
create policy "forum_posts_insert_own"
  on public.forum_posts for insert to authenticated
  with check (auth.uid() = author_id);

drop policy if exists "forum_posts_update_own" on public.forum_posts;
create policy "forum_posts_update_own"
  on public.forum_posts for update to authenticated
  using (auth.uid() = author_id)
  with check (auth.uid() = author_id);

drop policy if exists "forum_posts_delete_own" on public.forum_posts;
create policy "forum_posts_delete_own"
  on public.forum_posts for delete to authenticated
  using (auth.uid() = author_id);

drop policy if exists "forum_likes_select_authenticated" on public.forum_likes;
create policy "forum_likes_select_authenticated"
  on public.forum_likes for select to authenticated using (true);

drop policy if exists "forum_likes_insert_own" on public.forum_likes;
create policy "forum_likes_insert_own"
  on public.forum_likes for insert to authenticated
  with check (auth.uid() = user_id);

drop policy if exists "forum_likes_delete_own" on public.forum_likes;
create policy "forum_likes_delete_own"
  on public.forum_likes for delete to authenticated
  using (auth.uid() = user_id);

-- ---------------------------------------------------------------------------
-- Storage: forum-attachments/{user_id}/...
-- ---------------------------------------------------------------------------
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values (
  'forum-attachments',
  'forum-attachments',
  true,
  8388608,
  array['image/jpeg', 'image/png', 'image/webp', 'image/gif']
)
on conflict (id) do update set
  public = excluded.public,
  file_size_limit = excluded.file_size_limit,
  allowed_mime_types = excluded.allowed_mime_types;

drop policy if exists "forum_attachments_select_authenticated" on storage.objects;
create policy "forum_attachments_select_authenticated"
  on storage.objects for select to authenticated
  using (bucket_id = 'forum-attachments');

drop policy if exists "forum_attachments_insert_own_folder" on storage.objects;
create policy "forum_attachments_insert_own_folder"
  on storage.objects for insert to authenticated
  with check (
    bucket_id = 'forum-attachments'
    and (storage.foldername(name))[1] = auth.uid()::text
  );

drop policy if exists "forum_attachments_update_own_folder" on storage.objects;
create policy "forum_attachments_update_own_folder"
  on storage.objects for update to authenticated
  using (
    bucket_id = 'forum-attachments'
    and (storage.foldername(name))[1] = auth.uid()::text
  )
  with check (
    bucket_id = 'forum-attachments'
    and (storage.foldername(name))[1] = auth.uid()::text
  );

drop policy if exists "forum_attachments_delete_own_folder" on storage.objects;
create policy "forum_attachments_delete_own_folder"
  on storage.objects for delete to authenticated
  using (
    bucket_id = 'forum-attachments'
    and (storage.foldername(name))[1] = auth.uid()::text
  );
