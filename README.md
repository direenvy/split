# Split

Shared expenses between friends, settled in the fewest possible payments.

Create a group, add the people in it, log who paid for what, and the balances update as you
go. When you are done, **Settle up** gives you the shortest list of payments that clears
everybody — not "everyone pays everyone back", but the provably minimal set.

Vue 3 front end, ASP.NET Core back end, one deployable.

---

## Running it

You need the **.NET SDK 10** and **Node 20+**.

```bash
git clone <this repo>
cd project-split
```

Build the front end once, then run the server:

```bash
cd src/Split.Client && npm install && npm run build
```

```bash
cd ../.. && dotnet run --project src/Split.Api
```

Open the address it prints — the app and the API are both there. `npm run build` writes into
`src/Split.Api/wwwroot`, which ASP.NET Core serves as static files.

### Developing the front end

Run the API in one terminal and the Vite dev server in another, and you get hot reload:

```bash
dotnet run --project src/Split.Api --urls http://127.0.0.1:5199
```

```bash
cd src/Split.Client && npm run dev
```

Vite proxies `/api` through to the backend, so the browser still sees a single origin.

### Signing in with Google (optional)

**The app runs perfectly well without this.** Sign-in only adds a list of the groups you have
created or opened; every other feature works anonymously, and groups are reachable by their
link either way. If no credentials are configured the whole feature switches itself off — the
API reports `signInAvailable: false` and the client hides the button rather than offering one
that cannot work. So you can clone this and run it, and nothing will be broken.

To turn it on you need your own Google OAuth client. It takes a few minutes:

1. Go to the [Google Cloud console](https://console.cloud.google.com/), create a project (or
   pick an existing one).
2. **APIs & Services → OAuth consent screen.** Choose **External**, give it a name and your
   email. While it is in *Testing* you must add your own Google account under **Test users**,
   or Google will refuse to sign you in.
3. **APIs & Services → Credentials → Create credentials → OAuth client ID**, type
   **Web application**.
4. Under **Authorised redirect URIs** add exactly:

   ```
   http://localhost:5244/signin-google
   ```

   That path is fixed by `CallbackPath` in [`src/Split.Api/Auth.cs`](src/Split.Api/Auth.cs),
   and the port is the one in `launchSettings.json`. The URI has to match character for
   character — a trailing slash or the wrong port gives you `redirect_uri_mismatch`. If you
   deploy, add the deployed origin's `/signin-google` as a second URI.
5. Copy the client ID and client secret and store them with user-secrets, which keeps them
   outside the project directory so they cannot be committed:

```bash
cd src/Split.Api && dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_CLIENT_ID.apps.googleusercontent.com"
```

```bash
cd src/Split.Api && dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_CLIENT_SECRET"
```

Restart the API and the **Sign in** button appears. `GET /api/auth/me` returning
`{"signInAvailable":true,...}` confirms the configuration was picked up.

In production set the same two values as environment variables instead —
`Authentication__Google__ClientId` and `Authentication__Google__ClientSecret` — and remember
to add the deployed callback URL to the OAuth client.

Nothing about sign-in is stored as a credential here: this app never sees a password, and the
only thing kept about you is your Google account id, your email and your display name.

### Deploying

The whole app is one container: [`Dockerfile`](Dockerfile) builds the Vue bundle, drops it
into `wwwroot`, and publishes the API around it. What comes out is a single process serving
the client and the API from one origin, so the URL the host gives you **is** the application —
there is no separate front end to deploy and nothing to point at a backend.

```bash
docker build -t split .
docker run -p 8080:8080 -v split-data:/data split
```

On [Render](https://render.com), [`render.yaml`](render.yaml) describes it: *New → Blueprint*,
point it at this repository. Render prompts for the two Google values, which are marked
`sync: false` so the client secret never enters the repository — leave them blank and the app
still runs, with sign-in switched off.

**On the free plan the database does not survive.** A persistent disk needs a paid instance,
every deploy gets a fresh filesystem, and a free service sleeps after 15 minutes without
traffic — so SQLite lives inside the container and every group disappears the next time it
wakes. Fine for showing the app works; no good for its actual purpose, which is sending
somebody a link that still works tomorrow. To fix it, uncomment the `disk:` block in
`render.yaml` and set `plan: starter`, and SQLite behaves exactly as it does locally.
(Render's free Postgres is not a way around this: those are deleted 30 days after creation.)

Afterwards, add the deployed callback to your Google OAuth client, alongside the localhost
one:

```
https://YOUR-SERVICE.onrender.com/signin-google
```

The free tier sleeps when idle, so the first request after a quiet spell takes 30–60 seconds
to wake. After that it is immediate.

### Tests

```bash
dotnet test
```

102 tests. 71 cover the domain — the settlement algorithm and the money arithmetic — and 31
cover the API, running against real SQLite rather than an in-memory fake, because most of
what can go wrong there is a thing SQLite specifically does or does not support.

---

## Why one deployable

The front end and the API are served by the same host, on the same origin, from one process.
This is a deliberate choice and it is the first thing worth explaining about the project.

Splitting them means two deployments to keep in step, a CORS policy to configure, an API base
URL that differs per environment, and mixed-content problems the moment one side is HTTPS and
the other is not. None of that is interesting work, and all of it breaks at the worst time.

With one origin, there is no CORS policy anywhere in this repository. The client calls
`/api/groups` with no host and no environment variable. Development looks like production
because Vite proxies `/api` to the same backend.

The cost is that the front end cannot be deployed to a CDN independently. For an app this
size that is not a cost worth paying anything to avoid.

---

## The interesting part: settling up

[`src/Split.Domain/DebtSimplifier.cs`](src/Split.Domain/DebtSimplifier.cs)

Four friends finish a trip. Aisyah is owed 30, Bala is owed 20, Chong owes 30, Dinesh owes 20.

The obvious algorithm — match the biggest debtor to the biggest creditor, repeat — pairs
Chong with Aisyah, then has Dinesh and Bala left needing two more payments between them.
Three payments. But the right answer is two: Chong pays Aisyah, Dinesh pays Bala, and the two
pairs never need to interact.

That generalises:

```
minimum payments = (people with a non-zero balance)
                 - (number of subsets the group splits into, where each subset sums to zero)
```

Each zero-sum subset of size *k* costs *k-1* payments to clear internally, and no payment ever
has to cross between subsets. So the more such subsets you find, the fewer payments you make.

Finding the maximum number of them is **NP-hard** — it contains the partition problem. There
is no polynomial trick. But real groups are small, so the implementation is exact where it can
be and honest where it cannot:

- **Up to 18 people**: a dynamic program over all 2ⁿ subsets, running in O(2ⁿ·n) rather than
  the O(3ⁿ) of the obvious submask enumeration. The answer is provably minimal.
- **Beyond 18**: greedy matching, which never exceeds *n-1* payments and is usually optimal
  anyway. This is documented in the code rather than hidden.

### Money never touches a float

`10.00` split three ways is `3.3333...`, which binary floating point cannot represent. Round
each share to `3.33` and the total is `9.99`; round up and it is `10.02`. Either way the
balances stop summing to zero, and a group whose balances do not cancel can never be settled.

So:

- Every amount is **whole cents in a `long`** inside the domain, and `decimal` at the edges.
  Never `double`, anywhere, at any layer.
- [`MoneySplitter`](src/Split.Domain/MoneySplitter.cs) gives everyone the floor of the
  division and then hands out the leftover cents one each, so the parts always add back up to
  exactly the original. `10.00` three ways is `3.34 / 3.33 / 3.33`.
- Which person absorbs the extra cent is decided by their position in the group's roster, not
  by the order the browser happened to send — so the split is reproducible.
- Amounts with sub-cent precision are **rejected**, not rounded. The caller knows better than
  the ledger does what to do about them.
- The SQLite columns are integers of cents. SQLite has no decimal type, so EF Core would
  otherwise store them as text, and any arithmetic that happened in SQL would go through a
  float.
- The browser does no money arithmetic at all, beyond one clearly-labelled "about X each"
  preview. Amounts are held as strings in the form and parsed into `decimal` server-side.

### How it is tested

[`tests/Split.Domain.Tests`](tests/Split.Domain.Tests) — 71 tests.

The settlement tests do not just check that the output looks right. They include a **second,
independent implementation** of the hard part, written the slow and obvious way (enumerate
every subset, recurse, memoise) in
[`Settlement.cs`](tests/Split.Domain.Tests/Settlement.cs). It is far too slow for production,
but it is easy to believe. Every settlement test compares the real algorithm against it.

On top of the hand-picked awkward cases — a single debtor against many creditors, one person
paying for everything, everyone already square, independent pairs, a zero-sum subgroup hidden
inside a larger group — there are **1,400 randomly generated groups** (sizes 2 to 8, fixed
seeds so failures reproduce) where every one is checked to:

- settle every balance to exactly zero,
- use exactly the minimum number of payments the brute-force oracle says is possible,
- never emit a negative, zero or sub-cent payment.

`MoneySplitter` is checked exhaustively over every amount from 0 to 1000 cents against every
group size from 1 to 12, asserting the shares always sum back to the original.

---

## What it does not do

Worth being straight about, since this is a portfolio project rather than a product.

- **Google sign-in is optional.** Signed-in users get a history of groups they create or open;
  sign-in does not restrict access. A group is reachable by anybody who has its URL. The slug
  is readable rather than secret, so do not put sensitive information in a group.
- **Anonymous groups expire after 90 days without activity.** Opening a group or changing its
  members or expenses resets the timer. Groups saved in a signed-in user's history are kept.
- **The database starts with `EnsureCreated()`**, with a small idempotent startup update for
  the sign-in and retention fields. This project still does not use EF migrations; a larger
  deployment should move to migrations before making further schema changes.
- **Expenses split evenly** between the people you pick. No percentages, shares or itemised
  receipts.
- **People can be added to a group but not removed**, because their past expenses would have
  to go somewhere sensible and that question has no obviously right answer.
- **Deleting a group is permanent and creator-only.** A group is reachable by its link, so
  merely opening one gives you a history row for it; if deletion checked for that row instead
  of for having created the group, following a link somebody sent you would be enough to
  destroy their data. Everyone else gets *Remove*, which only clears their own history.
- **There is no "mark as paid"** — settling is advice, not a recorded transaction.
- **Destructive actions confirm by a second tap**, not a dialog.

---

## Layout

```
Split.slnx
├── src/
│   ├── Split.Domain/      Pure C#. No dependencies, no framework, no I/O.
│   │                      Money, MoneySplitter, BalanceCalculator, DebtSimplifier.
│   ├── Split.Api/         ASP.NET Core 10 minimal APIs + EF Core/SQLite.
│   │                      Serves the built client from wwwroot.
│   └── Split.Client/      Vue 3, Vite, TypeScript, Pinia, Vue Router.
├── tests/
│   └── Split.Domain.Tests/  xUnit.
└── docs/
    └── REACT-TO-VUE.md
```

The domain project references nothing. That is what lets the rules be tested without a
database, a web host or a mock in sight, and it is why the test suite runs in under a second.

There is no service layer between the HTTP handlers and the database. The only real logic
lives in the domain; a layer whose job was to forward calls would be ceremony, not structure.

### The API

| | |
|---|---|
| `POST /api/groups` | create a group with its members; returns the slug |
| `GET /api/groups/{slug}` | the group, its expenses, live balances and the settlement |
| `POST /api/groups/{slug}/members` | add a person |
| `POST /api/groups/{slug}/expenses` | log an expense |
| `DELETE /api/groups/{slug}/expenses/{id}` | remove an expense |
| `DELETE /api/groups/{slug}` | delete the group, creator only |
| `DELETE /api/groups/{slug}/membership` | drop it from your own history |
| `GET /api/groups/mine` | your groups; the only endpoint that needs an account |
| `GET /api/auth/me` | who is signed in, and whether sign-in is even available here |
| `GET /api/auth/sign-in` | start the Google redirect |
| `POST /api/auth/sign-out` | end the session |
| `POST /api/settlement-preview` | settle a set of balances without saving anything |

The five endpoints that read or change a group all return the whole group. The client
therefore never patches its own copy of the state to match the server, and what is on screen
cannot drift from what was computed. The auth endpoints and the preview are the exceptions,
because neither is about a particular group.

Balances and the settlement are **derived on read**, never stored — they are a pure function
of the expenses, and a stored copy would only be something to go stale.

Each expense's shares, by contrast, **are** stored, worked out once when the expense is
logged. If the splitting rule ever changes, old expenses keep the numbers the group already
agreed on.

---

## The front end

Vue 3 with `<script setup>` and TypeScript throughout. No Options API anywhere.

Six routes:

| | |
|---|---|
| `/` | the landing page, with a live demo of the settlement algorithm |
| `/new` | create a group, or open one by its code |
| `/groups` | your history, if you are signed in |
| `/g/:slug` | balances and the expense log |
| `/g/:slug/add` | log an expense |
| `/g/:slug/settle` | the settlement |

No route is guarded. `/groups` is the only screen that needs an account and it handles being
signed out by offering to sign in, which beats bouncing somebody to a login page they did not
ask for. Access is decided on the server; a router guard is a convenience, never a control.

Deep links work because the server falls back to `index.html` for any path it does not
recognise — except under `/api`, which stays a JSON 404 so a mistyped call does not come back
as an HTML page and surface as a baffling JSON parse error.

**[`docs/REACT-TO-VUE.md`](docs/REACT-TO-VUE.md)** maps all of this onto React, file by file.

---

## Design

The visual language is **Dala**, applied from an extracted token set: pure black canvas, a
single violet (`#8052ff`) for the one primary action on a screen, amber (`#ffb829`) for
emphasis only, a 6px spacing base, 24px radii, headings at weight 400 with heavy negative
tracking, body text at weight 200.

Those tokens came from a **marketing site**, and three of its rules had to be adapted rather
than applied. Each is commented where it happens, in
[`theme.css`](src/Split.Client/src/styles/theme.css).

**1. "No cards, no borders" does not survive a list of expenses.**
The source system floats one or two elements in a black void per viewport. That is right for
a headline and wrong for fifteen rows of figures, where the eye needs to know where one row
ends and the next begins. Rather than adding borders, surfaces are lifted a few points off
black (`#0a0a0a`, `#111`) with a 7%-opacity hairline between rows. The void is still the
dominant surface; the separation is just enough to scan.

**2. Display type is capped at 48px, not 113px.**
113px is a landing-page headline. The largest thing on these screens is a balance figure. The
scale is kept, including the `-0.04em` tracking that gives the big type its character, but the
top step is dropped to something that fits a data screen.

**3. Weight 200 is for prose, not for numbers.**
Ultra-light 18px body text is the signature of this system and it is lovely in a sentence. In
a column of money it is the wrong call — thin strokes make digits harder to tell apart, and
comparing figures is the entire job of this app. So prose keeps weight 200, and anything
numeric steps up to 400 with `tabular-nums` so the columns line up.

### Two other things about the token set

**The font is substituted.** The tokens specify PPNeueMontreal, which is a commercial
typeface from Pangram Pangram and cannot be redistributed in a public repository. The token
file itself names Inter as the fallback; this uses **Inter Variable**, self-hosted via
`@fontsource-variable/inter`, because the design depends on weight 200 and only the variable
cut actually has it. Bundling it rather than linking Google Fonts also keeps the app a single
deployable with no third-party request on first paint.

**There was a bug in the extracted tokens.** `surface.deep-verdant-tint` was `#15846` — five
hex digits, which is not a valid colour, so every browser would have dropped the rule
silently. The same colour appears correctly elsewhere in the file as `#15846e`. Corrected,
and noted in `theme.css` so the next person does not have to rediscover it.
