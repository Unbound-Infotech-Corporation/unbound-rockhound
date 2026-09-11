-- Unbound Rockhound — Phase 3 Reddit discovery / Facebook directory / community mentions
-- Apply via MCP apply_migration / Dashboard / supabase db push
-- Service role bypasses RLS (edge function writes). Authenticated clients: SELECT only.

-- ---------------------------------------------------------------------------
-- Category: From Reddit (not region-scoped)
-- ---------------------------------------------------------------------------
insert into public.forum_categories (id, slug, name, description, icon_key, sort_order, is_region_scoped)
values (
  '11111111-1111-4111-8111-111111111105',
  'from-reddit',
  'From Reddit',
  'Curated subreddit finds (read-only). Unverified community chatter — not claims.',
  'reddit',
  5,
  false
)
on conflict (id) do update set
  slug = excluded.slug,
  name = excluded.name,
  description = excluded.description,
  icon_key = excluded.icon_key,
  sort_order = excluded.sort_order,
  is_region_scoped = excluded.is_region_scoped;

-- ---------------------------------------------------------------------------
-- reddit_discoveries (NOT merged with claims / rumoured stores)
-- ---------------------------------------------------------------------------
create table if not exists public.reddit_discoveries (
  id uuid primary key default gen_random_uuid(),
  reddit_fullname_id text not null,
  subreddit text not null,
  title text not null,
  snippet text not null default '',
  url text not null,
  score int not null default 0,
  posted_at timestamptz,
  fetched_at timestamptz not null default now(),
  constraint reddit_discoveries_fullname_unique unique (reddit_fullname_id),
  constraint reddit_discoveries_snippet_len check (char_length(snippet) <= 600),
  constraint reddit_discoveries_title_len check (char_length(title) between 1 and 500),
  constraint reddit_discoveries_subreddit_len check (char_length(subreddit) between 1 and 80)
);

create index if not exists reddit_discoveries_fetched_idx
  on public.reddit_discoveries (fetched_at desc);
create index if not exists reddit_discoveries_subreddit_idx
  on public.reddit_discoveries (subreddit);

alter table public.reddit_discoveries enable row level security;

drop policy if exists "reddit_discoveries_select_authenticated" on public.reddit_discoveries;
create policy "reddit_discoveries_select_authenticated"
  on public.reddit_discoveries for select to authenticated using (true);

-- No insert/update/delete policies for authenticated — edge / service role only.

-- ---------------------------------------------------------------------------
-- facebook_directory (public group links; admin edits via SQL)
-- ---------------------------------------------------------------------------
create table if not exists public.facebook_directory (
  id uuid primary key default gen_random_uuid(),
  name text not null,
  url text not null,
  region_tag text,
  notes text not null default '',
  sort_order int not null default 0,
  is_active boolean not null default true,
  updated_at timestamptz not null default now(),
  constraint facebook_directory_name_len check (char_length(name) between 1 and 120),
  constraint facebook_directory_url_len check (char_length(url) between 8 and 500),
  constraint facebook_directory_region_len check (region_tag is null or char_length(region_tag) <= 80)
);

create or replace function public.set_facebook_directory_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

drop trigger if exists facebook_directory_set_updated_at on public.facebook_directory;
create trigger facebook_directory_set_updated_at
  before update on public.facebook_directory
  for each row execute function public.set_facebook_directory_updated_at();

alter table public.facebook_directory enable row level security;

drop policy if exists "facebook_directory_select_active_authenticated" on public.facebook_directory;
create policy "facebook_directory_select_active_authenticated"
  on public.facebook_directory for select to authenticated
  using (is_active = true);

-- No client writes.

insert into public.facebook_directory (id, name, url, region_tag, notes, sort_order, is_active)
values
  (
    '22222222-2222-4222-8222-222222222201',
    'Rockhounding (Facebook group)',
    'https://www.facebook.com/groups/rockhounding/',
    null,
    'Public group link — verify still open; admin may edit via SQL.',
    1,
    true
  ),
  (
    '22222222-2222-4222-8222-222222222202',
    'Rockhounding USA',
    'https://www.facebook.com/groups/rockhoundingusa/',
    'USA',
    'Public USA-focused rockhounding group (verify membership rules).',
    2,
    true
  ),
  (
    '22222222-2222-4222-8222-222222222203',
    'PNW Rockhounds',
    'https://www.facebook.com/groups/pnwrockhounds/',
    'PNW',
    'Pacific Northwest public group example.',
    3,
    true
  ),
  (
    '22222222-2222-4222-8222-222222222204',
    'PLACEHOLDER — Southwest Rockhounding Club',
    'https://www.facebook.com/groups/PLACEHOLDER-sw-rockhounding/',
    'Southwest',
    'PLACEHOLDER: replace URL with a real public group before promoting in-app.',
    4,
    true
  ),
  (
    '22222222-2222-4222-8222-222222222205',
    'PLACEHOLDER — Midwest Gem & Mineral',
    'https://www.facebook.com/groups/PLACEHOLDER-midwest-gem-mineral/',
    'Midwest',
    'PLACEHOLDER: admin edit via SQL when a stable public group URL is known.',
    5,
    true
  )
on conflict (id) do nothing;

-- ---------------------------------------------------------------------------
-- community_location_mentions (community-sourced, unverified — NEVER merge with claims)
-- ---------------------------------------------------------------------------
create table if not exists public.community_location_mentions (
  id uuid primary key default gen_random_uuid(),
  reddit_discovery_id uuid references public.reddit_discoveries (id) on delete set null,
  source_url text not null,
  source_label text not null default '',
  mention_text text not null,
  lat double precision,
  lon double precision,
  place_hint text,
  confidence real not null default 0.3,
  created_at timestamptz not null default now(),
  constraint community_mentions_text_len check (char_length(mention_text) between 1 and 1000),
  constraint community_mentions_url_len check (char_length(source_url) between 8 and 800),
  constraint community_mentions_confidence check (confidence >= 0 and confidence <= 1)
);

create index if not exists community_mentions_coords_idx
  on public.community_location_mentions (lat, lon)
  where lat is not null and lon is not null;
create index if not exists community_mentions_discovery_idx
  on public.community_location_mentions (reddit_discovery_id);

alter table public.community_location_mentions enable row level security;

drop policy if exists "community_mentions_select_authenticated" on public.community_location_mentions;
create policy "community_mentions_select_authenticated"
  on public.community_location_mentions for select to authenticated using (true);

-- No client writes — service role / edge only.

-- ---------------------------------------------------------------------------
-- profiles.show_reddit_discovery (cross-platform toggle)
-- ---------------------------------------------------------------------------
alter table public.profiles
  add column if not exists show_reddit_discovery boolean not null default true;

comment on column public.profiles.show_reddit_discovery is
  'When false, clients hide the From Reddit forum category / discovery feed.';

comment on table public.reddit_discoveries is
  'Ingested Reddit posts for Forum From Reddit. Not claims; read-only for clients.';
comment on table public.community_location_mentions is
  'Unverified place hints from community sources. Never merge into claims/rumoured.';
comment on table public.facebook_directory is
  'Curated public Facebook rockhounding group links for Forum directory panel.';
