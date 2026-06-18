// Input validation + normalisation shared across the auth endpoints.

import { bad } from "./errors";

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// ISO 3166-1 alpha-2. Unknown codes are tolerated (signup proceeds with null),
// so this is used only to decide whether to keep the value or drop it.
const ISO_COUNTRIES = new Set([
  "AD","AE","AF","AG","AI","AL","AM","AO","AQ","AR","AS","AT","AU","AW","AX","AZ",
  "BA","BB","BD","BE","BF","BG","BH","BI","BJ","BL","BM","BN","BO","BQ","BR","BS",
  "BT","BV","BW","BY","BZ","CA","CC","CD","CF","CG","CH","CI","CK","CL","CM","CN",
  "CO","CR","CU","CV","CW","CX","CY","CZ","DE","DJ","DK","DM","DO","DZ","EC","EE",
  "EG","EH","ER","ES","ET","FI","FJ","FK","FM","FO","FR","GA","GB","GD","GE","GF",
  "GG","GH","GI","GL","GM","GN","GP","GQ","GR","GS","GT","GU","GW","GY","HK","HM",
  "HN","HR","HT","HU","ID","IE","IL","IM","IN","IO","IQ","IR","IS","IT","JE","JM",
  "JO","JP","KE","KG","KH","KI","KM","KN","KP","KR","KW","KY","KZ","LA","LB","LC",
  "LI","LK","LR","LS","LT","LU","LV","LY","MA","MC","MD","ME","MF","MG","MH","MK",
  "ML","MM","MN","MO","MP","MQ","MR","MS","MT","MU","MV","MW","MX","MY","MZ","NA",
  "NC","NE","NF","NG","NI","NL","NO","NP","NR","NU","NZ","OM","PA","PE","PF","PG",
  "PH","PK","PL","PM","PN","PR","PS","PT","PW","PY","QA","RE","RO","RS","RU","RW",
  "SA","SB","SC","SD","SE","SG","SH","SI","SJ","SK","SL","SM","SN","SO","SR","SS",
  "ST","SV","SX","SY","SZ","TC","TD","TF","TG","TH","TJ","TK","TL","TM","TN","TO",
  "TR","TT","TV","TW","TZ","UA","UG","UM","US","UY","UZ","VA","VC","VE","VG","VI",
  "VN","VU","WF","WS","YE","YT","ZA","ZM","ZW",
]);

// Top-most-common passwords. Blocking these is far more effective at stopping
// trivially-guessable creds than symbol/case rules (which are an anti-pattern).
const COMMON_PASSWORDS = new Set([
  "123456789012","password1234","qwertyuiop12","123456789123","111111111111",
  "passwordpassword","123412341234","letmeinplease","qwerty123456","000000000000",
  "iloveyou1234","admin1234567","welcome12345","monkey123456","password12345",
  "abc123456789","trustno12345","1234567890ab","football1234","baseball1234",
  "dragon123456","sunshine1234","princess1234","superman1234","whateveryousay",
  "qazwsxedc123","michael12345","shadowshadow","master123456","jennifer1234",
  "111111111111","12345678901a","qwertyqwerty","password!!!!","aaaaaaaaaaaa",
  "loveme123456","zxcvbnm12345","asdfghjkl123","000000000001","computer1234",
  "starwars1234","123123123123","letmeinnow12","hello1234567","freedom12345",
  "whatever1234","mustang12345","123qwe123qwe","battery123456","correcthorse",
]);

export function clip(s: unknown, max: number): string {
  if (typeof s !== "string") return "";
  return s.slice(0, max).trim();
}

export function normEmail(raw: unknown): string {
  const email = clip(raw, 200).toLowerCase();
  if (!EMAIL_RE.test(email)) throw bad("Please provide a valid email address.");
  return email;
}

export function validatePassword(raw: unknown): string {
  if (typeof raw !== "string") throw bad("Please provide a password.");
  if (raw.length < 12)
    throw bad("Password must be at least 12 characters.");
  if (raw.length > 256) throw bad("Password is too long.");
  if (COMMON_PASSWORDS.has(raw.toLowerCase()))
    throw bad("That password is too common. Please choose another.");
  return raw;
}

export function validateName(raw: unknown, field: string): string {
  const name = clip(raw, 80);
  if (!name) throw bad(`Please provide your ${field}.`);
  return name;
}

export function validateFirmName(raw: unknown): string {
  const firm = clip(raw, 160);
  if (!firm) throw bad("Please provide your firm name.");
  return firm;
}

// Unknown country → null (signup still proceeds), per spec.
export function normCountry(raw: unknown): string | null {
  const c = clip(raw, 2).toUpperCase();
  return ISO_COUNTRIES.has(c) ? c : null;
}

// Derive a url-safe slug from the firm name. Caller appends a suffix on
// collision.
export function slugify(name: string): string {
  const base = name
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[^\w\s-]/g, "")
    .replace(/[\s_-]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return base || "firm";
}
