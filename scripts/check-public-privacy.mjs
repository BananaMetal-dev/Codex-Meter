#!/usr/bin/env node
import { readFileSync } from "node:fs";
import { extname } from "node:path";
import { spawnSync } from "node:child_process";

const allowedEmailDomains = new Set([
  "users.noreply.github.com",
  "github.com",
  "example.com",
  "example.org",
  "example.net",
]);
const textExtensions = new Set([
  ".cjs", ".cs", ".csproj", ".css", ".html", ".ini", ".js", ".json",
  ".jsx", ".md", ".mjs", ".ps1", ".scss", ".sh", ".sln", ".toml",
  ".ts", ".tsx", ".txt", ".xml", ".yaml", ".yml",
]);
const blockedPatterns = [
  ["Windows user home path", /\b[A-Za-z]:[\\/]+Users[\\/]+(?!Public(?:[\\/]|$))[^\\/\s"'`<>|]+(?:[\\/]|$)/i],
  ["macOS user home path", /(^|[\s"'`(])\/Users\/(?!Shared(?:\/|$))[^/\s"'`<>|]+(?:\/|$)/m],
  ["Linux user home path", /(^|[\s"'`(])\/home\/[^/\s"'`<>|]+(?:\/|$)/m],
  ["private key", /-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----/],
  ["GitHub token", /\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})/],
  ["OpenAI API key", /\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{30,}/],
  ["AWS access key", /\b(?:AKIA|ASIA)[A-Z0-9]{16}\b/],
  ["JWT", /\beyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{15,}/],
];
const emailPattern = /[A-Za-z0-9._%+-]+@([A-Za-z0-9.-]+\.[A-Za-z]{2,})/g;

function git(args, allowFailure = false) {
  const result = spawnSync("git", args, { encoding: "utf8" });
  if (!allowFailure && result.status !== 0) {
    process.stderr.write(result.stderr || "Git command failed.\n");
    process.exit(2);
  }
  return result;
}

function isAllowedEmail(email) {
  const at = email.lastIndexOf("@");
  return at >= 0 && allowedEmailDomains.has(email.slice(at + 1).toLowerCase());
}

function lineNumber(text, index) {
  return text.slice(0, index).split("\n").length;
}

function scanFiles(mode) {
  const args = mode === "staged"
    ? ["diff", "--cached", "--name-only", "--diff-filter=ACMR", "-z"]
    : ["ls-files", "-z"];
  const names = git(args).stdout.split("\0").filter(Boolean);
  const problems = [];

  for (const name of names) {
    if (!textExtensions.has(extname(name).toLowerCase())) continue;
    let buffer;
    if (mode === "staged") {
      const result = spawnSync("git", ["show", `:${name}`], { encoding: null });
      if (result.status !== 0) continue;
      buffer = result.stdout;
    } else {
      buffer = readFileSync(name);
    }
    if (buffer.includes(0)) continue;
    const text = buffer.toString("utf8");

    for (const [label, pattern] of blockedPatterns) {
      const match = pattern.exec(text);
      if (match) problems.push(`${name}:${lineNumber(text, match.index)}: ${label}`);
    }
    emailPattern.lastIndex = 0;
    for (const match of text.matchAll(emailPattern)) {
      if (!isAllowedEmail(match[0])) {
        problems.push(`${name}:${lineNumber(text, match.index)}: non-approved email address`);
      }
    }
  }
  return problems;
}

function scanConfiguredIdentity() {
  const result = git(["config", "--get", "user.email"], true);
  const email = result.stdout.trim();
  return email && isAllowedEmail(email)
    ? []
    : ["Git user.email must use an approved non-personal address."];
}

function scanCommits(range) {
  if (!range) return ["A commit range is required."];
  const result = git(["log", "--format=%H%x00%ae%x00%ce", range], true);
  if (result.status !== 0) return ["The requested commit range could not be inspected."];
  const problems = [];
  for (const row of result.stdout.split("\n").filter(Boolean)) {
    const [hash, authorEmail, committerEmail] = row.split("\0");
    if (!isAllowedEmail(authorEmail || "")) {
      problems.push(`${hash}: author email is not approved`);
    }
    if (!isAllowedEmail(committerEmail || "")) {
      problems.push(`${hash}: committer email is not approved`);
    }
  }
  return problems;
}

const args = process.argv.slice(2);
let problems = [];
if (args.includes("--staged")) {
  problems = [...scanConfiguredIdentity(), ...scanFiles("staged")];
} else if (args.includes("--all")) {
  problems = scanFiles("all");
} else if (args.includes("--commits")) {
  problems = scanCommits(args[args.indexOf("--commits") + 1]);
} else {
  process.stderr.write("Usage: check-public-privacy.mjs --all | --staged | --commits <range>\n");
  process.exit(2);
}

if (problems.length) {
  process.stderr.write("Public privacy scan failed:\n");
  for (const problem of problems) process.stderr.write(`- ${problem}\n`);
  process.stderr.write("Matched values are intentionally not printed.\n");
  process.exit(1);
}

process.stdout.write("Public privacy scan passed.\n");
