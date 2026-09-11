-- Unbound Rockhound — Phase 4 product licenses (Stripe one-time purchase)
-- Apply via Dashboard SQL or: supabase db push
-- Edge functions use service role; clients never read these tables directly.

-- ---------------------------------------------------------------------------
-- Licenses
-- ---------------------------------------------------------------------------
create table if not exists public.product_licenses (
  id uuid primary key default gen_random_uuid(),
  product_id text not null default 'unbound-rockhound',
  license_key text not null,
  email text,
  stripe_checkout_session_id text,
  stripe_payment_intent_id text,
  stripe_customer_id text,
  stripe_charge_id text,
  status text not null default 'active'
    check (status in ('active', 'revoked', 'refunded')),
  max_activations int not null default 3
    check (max_activations between 1 and 20),
  notes text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  revoked_at timestamptz,
  constraint product_licenses_key_format check (
    license_key ~ '^UR-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$'
  )
);

create unique index if not exists product_licenses_key_uidx
  on public.product_licenses (license_key);

create unique index if not exists product_licenses_session_uidx
  on public.product_licenses (stripe_checkout_session_id)
  where stripe_checkout_session_id is not null;

create index if not exists product_licenses_email_idx
  on public.product_licenses (lower(email));

create index if not exists product_licenses_payment_idx
  on public.product_licenses (stripe_payment_intent_id)
  where stripe_payment_intent_id is not null;

create or replace function public.set_product_licenses_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

drop trigger if exists product_licenses_set_updated_at on public.product_licenses;
create trigger product_licenses_set_updated_at
  before update on public.product_licenses
  for each row execute function public.set_product_licenses_updated_at();

-- ---------------------------------------------------------------------------
-- Activations (seat / device)
-- ---------------------------------------------------------------------------
create table if not exists public.license_activations (
  id uuid primary key default gen_random_uuid(),
  license_id uuid not null references public.product_licenses (id) on delete cascade,
  device_fingerprint text not null,
  device_label text,
  activated_at timestamptz not null default now(),
  last_seen_at timestamptz not null default now(),
  deactivated_at timestamptz,
  constraint license_activations_fp_len check (
    char_length(device_fingerprint) between 8 and 128
  )
);

create unique index if not exists license_activations_active_device_uidx
  on public.license_activations (license_id, device_fingerprint)
  where deactivated_at is null;

create index if not exists license_activations_license_idx
  on public.license_activations (license_id);

-- ---------------------------------------------------------------------------
-- Stripe webhook idempotency
-- ---------------------------------------------------------------------------
create table if not exists public.stripe_webhook_events (
  event_id text primary key,
  event_type text not null,
  processed_at timestamptz not null default now(),
  payload_summary text
);

-- ---------------------------------------------------------------------------
-- RLS — deny all direct client access (Edge functions use service role)
-- ---------------------------------------------------------------------------
alter table public.product_licenses enable row level security;
alter table public.license_activations enable row level security;
alter table public.stripe_webhook_events enable row level security;

revoke all on public.product_licenses from anon, authenticated;
revoke all on public.license_activations from anon, authenticated;
revoke all on public.stripe_webhook_events from anon, authenticated;

grant select, insert, update, delete on public.product_licenses to service_role;
grant select, insert, update, delete on public.license_activations to service_role;
grant select, insert, update, delete on public.stripe_webhook_events to service_role;
