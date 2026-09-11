-- Unbound Rockhound — profile marketplace shops (Etsy, eBay, etc.) + media
-- Apply via Dashboard SQL or MCP apply_migration

-- ---------------------------------------------------------------------------
-- Shops linked on a rockhound profile
-- ---------------------------------------------------------------------------
create table if not exists public.profile_shops (
  id uuid primary key default gen_random_uuid(),
  profile_id uuid not null references public.profiles (id) on delete cascade,
  marketplace text not null
    check (marketplace in (
      'etsy', 'ebay', 'shopify', 'whatnot', 'mercari',
      'facebook_marketplace', 'amazon_handmade', 'poshmark',
      'website', 'instagram_shop', 'other'
    )),
  shop_name text not null default '',
  shop_url text not null,
  description text not null default '',
  sort_order int not null default 0,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint profile_shops_name_len check (char_length(shop_name) <= 120),
  constraint profile_shops_url_len check (char_length(shop_url) between 8 and 500),
  constraint profile_shops_desc_len check (char_length(description) <= 1000),
  constraint profile_shops_url_http check (shop_url ~* '^https?://')
);

create index if not exists profile_shops_profile_idx
  on public.profile_shops (profile_id, sort_order);

create or replace function public.set_profile_shops_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

drop trigger if exists profile_shops_set_updated_at on public.profile_shops;
create trigger profile_shops_set_updated_at
  before update on public.profile_shops
  for each row execute function public.set_profile_shops_updated_at();

-- Max 12 shops per profile (app also enforces)
create or replace function public.enforce_profile_shop_limit()
returns trigger
language plpgsql
as $$
declare
  n int;
begin
  select count(*) into n from public.profile_shops where profile_id = new.profile_id;
  if tg_op = 'INSERT' and n >= 12 then
    raise exception 'Maximum of 12 marketplace shops per profile';
  end if;
  return new;
end;
$$;

drop trigger if exists profile_shops_limit on public.profile_shops;
create trigger profile_shops_limit
  before insert on public.profile_shops
  for each row execute function public.enforce_profile_shop_limit();

-- ---------------------------------------------------------------------------
-- Images / videos for a shop listing
-- ---------------------------------------------------------------------------
create table if not exists public.profile_shop_media (
  id uuid primary key default gen_random_uuid(),
  shop_id uuid not null references public.profile_shops (id) on delete cascade,
  media_type text not null check (media_type in ('image', 'video')),
  media_url text not null,
  caption text not null default '',
  sort_order int not null default 0,
  created_at timestamptz not null default now(),
  constraint profile_shop_media_url_len check (char_length(media_url) between 8 and 800),
  constraint profile_shop_media_caption_len check (char_length(caption) <= 200),
  constraint profile_shop_media_url_http check (media_url ~* '^https?://')
);

create index if not exists profile_shop_media_shop_idx
  on public.profile_shop_media (shop_id, sort_order);

-- Max 8 media items per shop
create or replace function public.enforce_shop_media_limit()
returns trigger
language plpgsql
as $$
declare
  n int;
begin
  select count(*) into n from public.profile_shop_media where shop_id = new.shop_id;
  if tg_op = 'INSERT' and n >= 8 then
    raise exception 'Maximum of 8 media items per shop';
  end if;
  return new;
end;
$$;

drop trigger if exists profile_shop_media_limit on public.profile_shop_media;
create trigger profile_shop_media_limit
  before insert on public.profile_shop_media
  for each row execute function public.enforce_shop_media_limit();

-- ---------------------------------------------------------------------------
-- RLS
-- ---------------------------------------------------------------------------
alter table public.profile_shops enable row level security;
alter table public.profile_shop_media enable row level security;

drop policy if exists "profile_shops_select_authenticated" on public.profile_shops;
create policy "profile_shops_select_authenticated"
  on public.profile_shops for select to authenticated
  using (true);

drop policy if exists "profile_shops_insert_own" on public.profile_shops;
create policy "profile_shops_insert_own"
  on public.profile_shops for insert to authenticated
  with check (auth.uid() = profile_id);

drop policy if exists "profile_shops_update_own" on public.profile_shops;
create policy "profile_shops_update_own"
  on public.profile_shops for update to authenticated
  using (auth.uid() = profile_id)
  with check (auth.uid() = profile_id);

drop policy if exists "profile_shops_delete_own" on public.profile_shops;
create policy "profile_shops_delete_own"
  on public.profile_shops for delete to authenticated
  using (auth.uid() = profile_id);

drop policy if exists "profile_shop_media_select_authenticated" on public.profile_shop_media;
create policy "profile_shop_media_select_authenticated"
  on public.profile_shop_media for select to authenticated
  using (true);

drop policy if exists "profile_shop_media_insert_own" on public.profile_shop_media;
create policy "profile_shop_media_insert_own"
  on public.profile_shop_media for insert to authenticated
  with check (
    exists (
      select 1 from public.profile_shops s
      where s.id = shop_id and s.profile_id = auth.uid()
    )
  );

drop policy if exists "profile_shop_media_update_own" on public.profile_shop_media;
create policy "profile_shop_media_update_own"
  on public.profile_shop_media for update to authenticated
  using (
    exists (
      select 1 from public.profile_shops s
      where s.id = shop_id and s.profile_id = auth.uid()
    )
  )
  with check (
    exists (
      select 1 from public.profile_shops s
      where s.id = shop_id and s.profile_id = auth.uid()
    )
  );

drop policy if exists "profile_shop_media_delete_own" on public.profile_shop_media;
create policy "profile_shop_media_delete_own"
  on public.profile_shop_media for delete to authenticated
  using (
    exists (
      select 1 from public.profile_shops s
      where s.id = shop_id and s.profile_id = auth.uid()
    )
  );

-- ---------------------------------------------------------------------------
-- Storage: shop-media/{user_id}/{shop_id}/...
-- ---------------------------------------------------------------------------
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values (
  'shop-media',
  'shop-media',
  true,
  52428800,
  array[
    'image/jpeg', 'image/png', 'image/webp', 'image/gif',
    'video/mp4', 'video/webm', 'video/quicktime'
  ]
)
on conflict (id) do update set
  public = excluded.public,
  file_size_limit = excluded.file_size_limit,
  allowed_mime_types = excluded.allowed_mime_types;

drop policy if exists "shop_media_select_authenticated" on storage.objects;
create policy "shop_media_select_authenticated"
  on storage.objects for select to authenticated
  using (bucket_id = 'shop-media');

drop policy if exists "shop_media_insert_own" on storage.objects;
create policy "shop_media_insert_own"
  on storage.objects for insert to authenticated
  with check (
    bucket_id = 'shop-media'
    and (storage.foldername(name))[1] = auth.uid()::text
  );

drop policy if exists "shop_media_update_own" on storage.objects;
create policy "shop_media_update_own"
  on storage.objects for update to authenticated
  using (
    bucket_id = 'shop-media'
    and (storage.foldername(name))[1] = auth.uid()::text
  );

drop policy if exists "shop_media_delete_own" on storage.objects;
create policy "shop_media_delete_own"
  on storage.objects for delete to authenticated
  using (
    bucket_id = 'shop-media'
    and (storage.foldername(name))[1] = auth.uid()::text
  );
