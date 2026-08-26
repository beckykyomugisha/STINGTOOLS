# Planscape — Go-to-Market Strategy

**Date:** 2026-08-26 · **Author:** research pass for planscape.build
**Goal as stated:** every engineer and architect in the region reaching for Planscape by default.

---

## 1. The diagnosis

Three facts about the current setup decide everything below.

**a) The site sells to a firm; the person who adopts a Revit tool is an individual.**
Pricing starts at **$60/firm/mo (Solo, ≤3 coordinators)** and rises to $1,000. Every plan is
gated behind a 14-day trial. But nobody in this market has ever been persuaded to buy BIM
software by a pricing page. It happens the other way round: one engineer installs a plugin,
saves four hours on a tag run, shows a colleague, and six months later the practice buys the
cloud. **There is currently no way for that first engineer to start.** The trial is time-boxed,
firm-scoped and asks for a company decision on day one.

**b) The competition is not Revizto or ACC. It is not doing BIM at all.**
BIM adoption across Africa is slowed primarily by *lack of awareness*, not by a crowded vendor
field. That is a completely different marketing job. You do not win a comparison — you create
the category locally, and the firm that creates a category owns it. Competing on ACC feature
parity is the losing frame; teaching ISO 19650 and handing over the tool that implements it is
the winning one.

**c) The strongest asset is unmonetised: ~1,580 commands of real Revit tooling.**
STING Tools is a genuinely large plugin. Every one of those commands is a search query somebody
types into Google or YouTube at 11pm before a deadline. That is a distribution engine sitting
idle behind a paywall.

---

## 2. The wedge: split the product in two

> **Give away the plugin. Sell the coordination.**

| Layer | Who pays | Purpose |
|---|---|---|
| **STING Tools Free** — individual, forever-free, ~40–60 of the best single-user commands (tagging, sheet manager, selection, colour-by-parameter, schedules, BOQ export) | nobody | acquisition + habit |
| **STING Tools Pro** — full ~1,580 commands, $12–15/user/mo | the individual, on their own card | revenue floor + upgrade path |
| **Planscape Cloud** — CDE, mobile, GPS capture, BCF, clash, 4D/5D — current firm pricing | the practice | the real business |

The free tier is not charity, it is the top of the funnel. It only works if it is genuinely
useful alone — a crippled demo teaches people the tool is weak. Ship the tagging engine, the
sheet manager and the selection tools free and unlimited. Gate anything that needs a *second
person*: sync, issues, transmittals, mobile, clash. That line is honest and easy to explain —
**free for you, paid for your team** — and it makes the upgrade a moment of genuine need rather
than an artificial wall.

Keep the current firm plans exactly as they are. Add the individual tiers beneath them.

---

## 3. Channels, ranked by return on effort

### Tier 1 — do these first

**1. Autodesk App Store listing.** The single highest-leverage distribution move available.
It puts Planscape in front of every Revit user on earth at zero CAC and lends instant
credibility ("it's on the Autodesk store" ends the is-this-safe conversation with an IT
manager). Requirements are compatible with the free-tier plan: the app must be **relevant to
and run on Revit 2026** (2021–2025 can be listed as additional compatibility), must be
**usable immediately after install with no manual file copying or registration**, and
**licensing is the publisher's responsibility** — so a free tier that works on install with a
sign-in for Pro is exactly the shape they expect. STINGTOOLS already targets 2025/2026/2027.
*Blocker to clear first: the installer. `deploy.bat` + manual `.addin` editing will not pass;
you need a signed MSI.*

**2. Become a CPD-accredited training provider.** This is the regional unlock and almost
nobody does it well. In Kenya, BORAQS requires registered architects and quantity surveyors to
earn **30 CPD points per year, at least 20 from BORAQS-organised activity**, to renew their
practising certificate. Engineers' boards run comparable regimes. That means there is a
standing, legally-compelled audience of exactly your buyers who must sit in a room several
times a year. Run "ISO 19650 in Practice" as an accredited course, taught in Revit, using
Planscape as the worked example. You are not advertising to them — you are the CPD they
needed anyway, and you leave with a delegate list of registered professionals.
Do this in Kampala, Nairobi, Dar and Kigali. It is the best money you will spend.

**3. Turn the 20 guides into search and video.** `marketing-site/guides/` already holds 20
solid documents and `tutorials/` holds 5. That is a content library most startups would pay
$30k to commission, currently earning nothing because it is written for people who already
signed up. Rewrite the top ten as *problem-first* public articles — "How to tag 4,000 elements
to ISO 19650 in Revit", "How to build a BOQ from a Revit model", "Panel schedules Revit won't
let you edit" — and film each as a 6–10 minute screen recording. Long-tail Revit how-to search
has consistent global volume, near-zero competition on the ISO 19650 and BOQ terms, and the
viewer is a qualified Revit user with a live problem. Every video ends with the free plugin.
**Two videos a month, forever, is the whole YouTube strategy.** Do not overthink production.

### Tier 2 — build through Q1

**4. The Kampala Uganda Temple as flagship proof.** You have a real, prestigious,
multi-discipline project already running on STING with a full BEP, MIDP and KPI dashboard.
Publish it as a proper case study with numbers: clashes caught, hours saved on drawing
production, tag compliance percentage over time. One credible local case study outperforms
fifty pages of feature copy in this market, because the objection you are actually fighting is
"does this work here, on projects like mine". Get client permission early; that conversation is
the long pole.

**5. Institutions and the summit circuit.** BIM Africa is the pan-African body with an
explicit mandate to educate and certify, it runs the annual **BIM Africa Summit** (2025 was
Nairobi), and it is the fastest route to a regional audience that already believes in BIM.
Sponsor and speak — speak first, sponsor second. Pair it with local bodies: UIPE and the Uganda
Society of Architects, AAK and IEK in Kenya. Aim to be the *technical partner* who runs the BIM
session, not a logo on a banner.

**6. University pipeline.** Free institutional licences to Makerere, Kyambogo, Nairobi, JKUAT,
Ardhi. Guest-lecture the final-year BIM module. Graduates arrive at their first job already
fluent, and juniors are who actually drive tool adoption inside a practice. Cost: near zero.
Payback: 2–3 years, then compounding permanently.

**7. WhatsApp, not email.** Support is already offered over WhatsApp — correct instinct, extend
it to marketing. Run a regional "BIM Uganda / BIM East Africa" WhatsApp community: one tip per
week, release notes, an open Q&A channel. In this market WhatsApp beats email newsletters by an
order of magnitude on open rate and is where professional referral actually happens. Newsletters
are the wrong default imported from US SaaS playbooks.

### Tier 3 — the long game with the biggest payoff

**8. Get BIM into procurement.** The decisive move is not a marketing campaign at all: it is
getting ISO 19650 information requirements written into public tender documents at UNRA, KCCA,
Ministry of Works, and the big donor-funded programmes. When a tender demands a BEP and a
structured CDE, every bidder needs the tooling — and you are the only vendor who has been
teaching the standard locally for two years. Route in: policy work with BIM Africa (which
explicitly targets BIM policy adoption in African countries) plus direct advisory to the boards.
Slow, unglamorous, and worth more than everything above combined.

**9. Regional partner network.** One implementation partner per market (Nairobi, Dar, Kigali,
Addis, Lagos) on 20–30% recurring commission, doing local training and onboarding. Do not open
this until the product onboards without you in the room.

---

## 4. Positioning

Current tagline — *"BIM coordination for East African firms"* — is right in spirit and limiting
in practice. It is a geography, not a promise, and it caps you at the regional market while the
Autodesk App Store makes you globally visible.

Two-line structure to use instead:

> **Line 1 (the promise, global):** ISO 19650 delivery that actually runs — Revit plugin,
> cloud CDE, and a mobile app that works with no signal.
> **Line 2 (the proof, local):** Built in Kampala for firms delivering real projects across
> East Africa. Pay in your own currency, by mobile money.

Lead with capability, close with locality. "Built in Kampala" is a credibility asset in
Nairobi and a differentiator in London — it is only a limitation if you use it as the headline.

**The offline-first mobile app is the most under-sold thing on the site.** Site connectivity is
a universal AEC problem, not an African one. Revizto and ACC both assume a connection. Lead
with it far more aggressively.

---

## 5. First 90 days

| Weeks | Do | Done when |
|---|---|---|
| 1–2 | Define the free/Pro command split; add individual tiers to pricing | Free tier live on the site |
| 1–4 | Build a signed MSI installer (blocks the App Store) | Silent install, no manual `.addin` edit |
| 3–6 | Submit to the Autodesk App Store | Listing published |
| 3–8 | Rewrite 10 guides as public SEO articles; film the first 4 videos | Indexed and on YouTube |
| 4–8 | Apply for CPD accreditation (Kenya first — clearest criteria) | Accredited provider status |
| 6–10 | KUT case study, with client sign-off | Published with real numbers |
| 8–12 | First CPD seminar, Kampala; launch WhatsApp community | 40+ registered professionals in a room |
| ongoing | 2 videos/month, 1 article/week, 1 WhatsApp tip/week | habit, not campaign |

---

## 6. What to measure

Track **free plugin installs** as the north star, not website visits or trial starts. Then:
weekly-active plugin users → free-to-Pro conversion → Pro-to-Cloud conversion (the only one
that matters commercially) → CPD delegates → delegate-to-install rate.

If free installs grow and Cloud conversion does not, the problem is the *team* value
proposition, not the funnel. Ignore vanity metrics entirely — LinkedIn impressions in
particular are noise in a market this small.

---

## 7. What not to do

- **Do not buy Google Ads against "Revit plugin" or "BIM coordination".** You will pay
  Autodesk-scale CPCs to reach a global audience you cannot yet support.
- **Do not gate the plugin behind "book a demo".** Every gate costs you the exact engineer
  you are trying to reach; they will not book a call to try a tagging tool.
- **Do not compete on feature count with ACC.** You lose that comparison and it is the wrong
  fight — win on price, offline capability, local support and ISO 19650 hand-holding.
- **Do not build a conventional email newsletter first.** WhatsApp, then email.
- **Do not scale sales before onboarding works unattended.** If a firm needs you on a call to
  get value, partners and ads will only manufacture churn faster.

---

## 8. The one-sentence version

Give the plugin away to individuals so engineers adopt it privately, get listed on the
Autodesk App Store so distribution costs nothing, become the accredited CPD provider so the
region's registered professionals are legally obliged to sit in your classroom — and sell the
cloud to the firms those engineers already work for.

---

### Sources

- [Revit publisher guidelines — Autodesk Platform Services](https://aps.autodesk.com/marketplace/publisher-center/revit-publisher-guidelines)
- [Autodesk App Store Publisher FAQ](https://damassets.autodesk.net/content/dam/autodesk/www/adn/pdf/frequently-asked-questions.pdf)
- [BIM Africa Initiative](https://bimafrica.org/)
- [BIM Africa Summit](https://summit.bimafrica.org/)
- [BORAQS CPD Points Guideline (Kenya)](https://boraqs.or.ke/wp-content/uploads/2021/07/CPD-Points-Guideline.pdf)
- [Uganda Engineers Registration Board](https://www.erb.go.ug/registered-engineers/)
